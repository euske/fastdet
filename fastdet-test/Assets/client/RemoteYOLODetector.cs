///  RemoteYOLODetector.cs
///
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace net.sss_consortium.fastdet {

public class RemoteYOLODetector : YOLODetector {

    private TcpClient _tcp;
    private UdpClient _udp;
    private byte[] _session_id;
    private MemoryStream _recv_buf;
    private uint _recv_seqno;
    private uint _send_seqno;

    private static byte[] JPEG_TYPE = {
        (byte)'J', (byte)'P', (byte)'E', (byte)'G'
    };
    private static byte[] RTP_DUMMY_PACKET = {
        0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    // Initializes the endpoint connection.
    public RemoteYOLODetector(string url) {
        // Parse the url.
        int i;
        if (!url.StartsWith("rtsp://")) {
            throw new ArgumentException("invalid url (no rtsp): url="+url);
        }
        i = url.IndexOf('/', 7);
        if (i < 0) {
            throw new ArgumentException("invalid url (no slash): url="+url);
        }
        string hostport = url.Substring(7, i-7);
        string path = url.Substring(i+1);
        i = hostport.IndexOf(':');
        if (i < 0) {
            throw new ArgumentException("invalid url (no colon): url="+url);
        }
        string host = hostport.Substring(0, i);
        int port = int.Parse(hostport.Substring(i+1));
        logit("Open: host={0}, port={1}, path={2}", host, port, path);

        // Connect the host.
        IPAddress addr = Dns.GetHostEntry(host).AddressList[0];
        IPEndPoint ep = new IPEndPoint(addr, port);
        _tcp = new TcpClient();
        _tcp.Connect(ep);

        // Send the request.
        _udp = new UdpClient(0);
        int lport = (_udp.Client.LocalEndPoint as IPEndPoint).Port;
        NetworkStream stream = _tcp.GetStream();
        string req = "FEED "+lport+" "+path+"\r\n";
        byte[] data = Encoding.UTF8.GetBytes(req);
        stream.Write(data, 0, data.Length);

        // Recv the response.
        byte[] buf = new byte[1024];
        int n = stream.Read(buf, 0, buf.Length);
        string resp = Encoding.UTF8.GetString(buf, 0, n).Trim();
        if (!resp.StartsWith("+OK ")) {
            throw new IOException("invalid response (not OK): resp="+resp);
        }
        i = resp.IndexOf(' ', 4);
        if (i < 0) {
            throw new IOException("invalid response (no port): resp="+resp);
        }
        int rtp_port = int.Parse(resp.Substring(4, i-4));
        string hexid = resp.Substring(i+1);
        logit("Open: rtp_port={0}, session_id={1}", rtp_port, hexid);
        _session_id = new byte[hexid.Length / 2];
        for (i = 0; i < _session_id.Length; i++) {
            _session_id[i] = byte.Parse(hexid.Substring(i*2, 2), NumberStyles.HexNumber);
        }
        _udp.Connect(addr, rtp_port);

        // Initialize the internal states.
        _recv_buf = null;
        _recv_seqno = 0;
        _send_seqno = 1;

        // Send the dummy packet to initiate the stream.
        _udp.Send(RTP_DUMMY_PACKET, RTP_DUMMY_PACKET.Length);
        _udp.BeginReceive(new AsyncCallback(onRecvRTP), _udp);
    }

    // Uninitializes the endpoint connection.
    public override void Dispose() {
        base.Dispose();
        if (_udp != null) {
            _udp.Close();
            _udp.Dispose();
            _udp = null;
        }
        if (_tcp != null) {
            _tcp.Close();
            _tcp.Dispose();
            _tcp = null;
        }
    }

    protected override void performDetection(YLRequest request, Texture2D pixels) {
        if (_udp == null) {
            Debug.LogWarning("requestRemoteDetection: connection not open.");
            return;
        }

        addRequest(request);
        byte[] data = pixels.EncodeToJPG();
        using (MemoryStream buf = new MemoryStream()) {
            writeBytes(buf, JPEG_TYPE);
            writeUInt32(buf, request.RequestId);
            writeUInt32(buf, (uint)(request.Threshold*100));
            writeUInt32(buf, (uint)data.Length);
            writeBytes(buf, data);
            sendRTP(buf.ToArray());
        }
    }

    private static void writeBytes(MemoryStream s, byte[] b) {
        s.Write(b, 0, b.Length);
    }

    private static void writeUInt16(MemoryStream s, uint v) {
        s.WriteByte((byte)((v >> 8) & 0xff));
        s.WriteByte((byte)((v >> 0) & 0xff));
    }

    private static void writeUInt32(MemoryStream s, uint v) {
        s.WriteByte((byte)((v >> 24) & 0xff));
        s.WriteByte((byte)((v >> 16) & 0xff));
        s.WriteByte((byte)((v >> 8) & 0xff));
        s.WriteByte((byte)((v >> 0) & 0xff));
    }

    private static int parseInt16(byte[] b, uint i) {
        int v = (b[i] << 8) | b[i+1];
        return (v < 32768)? v : (v-32768);
    }

    private static uint parseUInt16(byte[] b, uint i) {
        return ( ((uint)b[i] << 8) | ((uint)b[i+1]) );
    }

    private static uint parseUInt32(byte[] b, uint i) {
        return ( ((uint)b[i] << 24) | ((uint)b[i+1] << 16) |
                 ((uint)b[i+2] << 8) | ((uint)b[i+3]) );
    }

    private const int CHUNK_SIZE = 40000;
    private void sendRTP(byte[] data) {
        if (_udp == null) return;
        int i0 = 0;
        while (i0 < data.Length) {
            int i1 = Math.Min(i0 + CHUNK_SIZE, data.Length);
            byte pt = 96;
            if (data.Length <= i1) {
                pt |= 0x80;
            }
            using (MemoryStream buf = new MemoryStream()) {
                buf.WriteByte(0x80);
                buf.WriteByte(pt);
                writeUInt16(buf, _send_seqno);
                buf.Write(data, i0, i1-i0);
                _udp.Send(buf.ToArray(), (int)buf.Length);
            }
            _send_seqno = (_send_seqno == 0xffff)? 1 : (_send_seqno+1);
            i0 = i1;
        }
    }

    private void onRecvRTP(IAsyncResult ar) {
        if (_udp == null) return; // ???
        IPEndPoint ep = null;
        byte[] data = _udp.EndReceive(ar, ref ep);
        if (data.Length < 4) return; // error
        byte flags = data[0];
        byte pt = data[1];
        uint seqno = parseUInt16(data, 2);
        if (seqno == 0) {
            ;
        } else {
            if (_recv_seqno == 0) {
                _recv_buf = new MemoryStream();
                _recv_seqno = seqno;
            } else if (_recv_seqno != seqno) {
                logit("onRecvRTP: DROP: recv_seqno={0}, seqno={1}", _recv_seqno, seqno);
                _recv_buf = null;
            }
            if ((pt & 0x7f) == 96) {
                if (_recv_buf != null) {
                    _recv_buf.Write(data, 4, data.Length-4);
                }
            }
            if ((pt & 0x80) != 0) {
                if (_recv_buf != null) {
                    recvData(_recv_buf.ToArray());
                }
                _recv_buf = new MemoryStream();
            }
            _recv_seqno = (seqno == 0xffff)? 1 : (seqno+1);
        }
        _udp.BeginReceive(new AsyncCallback(onRecvRTP), _udp);
    }

    private void recvData(byte[] data) {
        if (data.Length < 16) return; // error
        if (data[0] == 'Y' && data[1] == 'O' && data[2] == 'L' && data[3] == 'O') {
            // Parse results.
            uint requestId = parseUInt32(data, 4);
            YLRequest request = removeRequest(requestId);
            if (request == null) return;
            Rect detectArea = request.DetectArea;
            uint msec = parseUInt32(data, 8);
            uint length = parseUInt32(data, 12);
            List<YLObject> objs = new List<YLObject>();
            for (uint n = 0; n+10 <= length; n += 10) {
                // Parse one object.
                uint i = 16+n;
                int klass = data[i];
                if (klass == 0 || LABELS.Length <= klass) continue;
                float conf = data[i+1];
                float x = parseInt16(data, i+2);
                float y = parseInt16(data, i+4);
                float w = parseInt16(data, i+6);
                float h = parseInt16(data, i+8);
                YLObject obj1 = new YLObject();
                obj1.Label = LABELS[klass];
                obj1.Conf = conf / 255f;
                obj1.BBox = new Rect(
                    detectArea.x+(x/request.ImageSize.x)*detectArea.width,
                    detectArea.y+(y/request.ImageSize.y)*detectArea.height,
                    (w/request.ImageSize.x)*detectArea.width,
                    (h/request.ImageSize.y)*detectArea.height);
                objs.Add(obj1);
            }
            YLResult result1 = new YLResult() {
                RequestId = requestId,
                SentTime = request.SentTime,
                RecvTime = DateTime.Now,
                InferenceTime = msec / 1000f,
                Objects = objs.ToArray(),
            };
            //logit("recvData: result1={0}", result1);
            addResult(result1);
        }
    }
}

} // namespace net.sss_consortium.fastdet
