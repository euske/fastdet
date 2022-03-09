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
using Unity.Barracuda;

namespace net.sss_consortium.fastdet {

public class RemoteYOLODetector : IObjectDetector {

    // Detection mode.
    public YLDetMode Mode { get; set; }
    // Detection threshold.
    public float Threshold { get; set; }

    private RenderTexture _scaleBuffer;
    private Texture2D _buffer;
    private TcpClient _tcp;
    private UdpClient _udp;
    private byte[] _session_id;
    private MemoryStream _recv_buf;
    private uint _recv_seqno;
    private uint _send_seqno;

    private uint _requestId = 0;
    private List<YLResult> _results = new List<YLResult>();

    public RemoteYOLODetector() {
        _scaleBuffer = new RenderTexture(416, 416, 0);
        _buffer = new Texture2D(_scaleBuffer.width, _scaleBuffer.height);
    }

    private static void logit(string fmt, params object[] args) {
        Debug.Log(string.Format(fmt, args));
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

    // Initializes the endpoint connection.
    public void Open(string url) {
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
        string req = "DETECT "+lport+" "+path+"\r\n";
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

        // Send the dummy packet to initiate the stream.
        byte[] packet = {
            0x80, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };
        _udp.Send(packet, packet.Length);
        _recv_buf = new MemoryStream();
        _recv_seqno = 0;
        _send_seqno = 1;
        _udp.BeginReceive(new AsyncCallback(onRecvRTP), _udp);
    }

    private const int CHUNK_SIZE = 32768;
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
            _send_seqno = (_send_seqno+1) & 0xffff;
            i0 = i1;
        }
    }

    private void onRecvRTP(IAsyncResult ar) {
        if (_udp == null) return;
        IPEndPoint ep = null;
        byte[] data = _udp.EndReceive(ar, ref ep);
        if (data.Length < 4) return;
        byte flags = data[0];
        byte pt = data[1];
        uint seqno = ((uint)data[2] << 8) | (uint)data[3];
        if (_recv_seqno != seqno) {
            logit("onRecvRTP: DROP {0}/{1}", seqno, _recv_seqno);
            if (_recv_buf != null) {
                _recv_buf.Dispose();
            }
            _recv_buf = null;
        }
        if ((pt & 0x7f) == 96 && _recv_buf != null) {
            _recv_buf.Write(data, 4, data.Length-4);
        }
        if ((pt & 0x80) != 0) {
            if (_recv_buf != null) {
                recvData(_recv_buf.ToArray());
            }
            _recv_buf = new MemoryStream();
        }
        _recv_seqno = (seqno+1) & 0xffff;
        _udp.BeginReceive(new AsyncCallback(onRecvRTP), _udp);
    }

    private void recvData(byte[] data) {
        logit("recvData: data={0}", data.Length);
    }

    // Uninitializes the endpoint connection.
    public void Dispose() {
        if (_scaleBuffer != null) {
            UnityEngine.Object.Destroy(_scaleBuffer);
            _scaleBuffer = null;
        }
        if (_buffer != null) {
            UnityEngine.Object.Destroy(_buffer);
            _buffer = null;
        }
        if (_tcp != null) {
            _tcp.Close();
            _tcp.Dispose();
            _tcp = null;
        }
        if (_udp != null) {
            _udp.Close();
            _udp.Dispose();
            _udp = null;
        }
        logit("Dispose");
    }

    // Sends the image to the queue and returns the request id;
    public uint DetectImage(Texture image) {
        // Resize the texture.
        Graphics.Blit(image, _scaleBuffer);
        return DetectImage(_scaleBuffer);
    }

    public uint DetectImage(RenderTexture rTex) {
        // Convert the texture.
        RenderTexture temp = RenderTexture.active;
        RenderTexture.active = rTex;
        _buffer.ReadPixels(new Rect(0, 0, _buffer.width, _buffer.height), 0, 0);
        _buffer.Apply();
        RenderTexture.active = temp;
        _requestId++;
        switch (Mode) {
        case YLDetMode.ClientOnly:
            performLocalDetection(_requestId, _buffer);
            break;
        case YLDetMode.ServerOnly:
            requestRemoteDetection(_requestId, _buffer);
            break;
        default:
            addDummyResult(_requestId);
            break;
        }
        return _requestId;
    }

    private void performLocalDetection(uint requestId, Texture2D buffer) {
        var data = new TextureAsTensorData(buffer, 3);
    }

    private void requestRemoteDetection(uint requestId, Texture2D buffer) {
        byte[] image = buffer.EncodeToJPG();
        using (MemoryStream buf = new MemoryStream()) {
            writeBytes(buf, new byte[] { (byte)'J', (byte)'P', (byte)'E', (byte)'G' });
            writeUInt32(buf, requestId);
            writeUInt32(buf, (uint)image.Length);
            writeBytes(buf, image);
            sendRTP(buf.ToArray());
        }
    }

    private void addDummyResult(uint requestId) {
        YLObject obj1 = new YLObject();
        obj1.Label = "cat";
        obj1.Conf = 1.0f;
        obj1.BBox = new Rect(10, 10, 100, 100);
        YLResult result1 = new YLResult();
        result1.RequestId = requestId;
        result1.SentTime = DateTime.Now;
        result1.RecvTime = DateTime.Now;
        result1.InferenceTime = 0;
        result1.Objects = new YLObject[] { obj1 };
        _results.Add(result1);
    }

    // Gets the results (if any).
    public YLResult[] GetResults() {
        YLResult[] results = _results.ToArray();
        _results.Clear();
        return results;
    }
}

} // namespace net.sss_consortium.fastdet
