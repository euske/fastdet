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

    internal struct Request {
        public uint RequestId;
        public DateTime SentTime;
        public Rect ClipRect;
        public override string ToString() {
            return string.Format(
                "<Request: RequstId={0}, SentTime={1}, ClipRect={2}>",
                RequestId, SentTime, ClipRect);
        }
    };

    // Detection mode.
    public YLDetMode Mode { get; set; }
    // Detection threshold.
    public float Threshold { get; set; } = 0.3f;

    private Model _model;
    private IWorker _worker;

    private RenderTexture _buffer;
    private Texture2D _pixels;
    private uint _requestId;
    private List<YLResult> _results;

    private TcpClient _tcp;
    private UdpClient _udp;
    private byte[] _session_id;
    private MemoryStream _recv_buf;
    private uint _recv_seqno;
    private uint _send_seqno;
    private Dictionary<uint, Request> _requests;

    private static byte[] RTP_DUMMY_PACKET = {
        0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    private const float REQUEST_TIMEOUT = 3f;

    private const int IMAGE_SIZE_WIDTH = 416;
    private const int IMAGE_SIZE_HEIGHT = 416;
    private static Vector2[] ANCHORS_FULL = new Vector2[] {
        new Vector2(116, 90),
        new Vector2(156, 198),
        new Vector2(373, 326),
        new Vector2(30, 61),
        new Vector2(62, 45),
        new Vector2(59, 119),
        new Vector2(10, 13),
        new Vector2(16, 30),
        new Vector2(33, 23),
    };
    private static Vector2[] ANCHORS_TINY = new Vector2[] {
        new Vector2(81, 82),
        new Vector2(135, 169),
        new Vector2(344, 319),
        new Vector2(10, 14),
        new Vector2(23, 27),
        new Vector2(37, 58),
    };
    private static string[] LABELS = {
        // COCO dataset labels.
        null,                   // UNDEFINED
        "person",
        "bicycle",
        "car",
        "motorbike",
        "aeroplane",
        "bus",
        "train",
        "truck",
        "boat",
        "traffic light",
        "fire hydrant",
        "stop sign",
        "parking meter",
        "bench",
        "bird",
        "cat",
        "dog",
        "horse",
        "sheep",
        "cow",
        "elephant",
        "bear",
        "zebra",
        "giraffe",
        "backpack",
        "umbrella",
        "handbag",
        "tie",
        "suitcase",
        "frisbee",
        "skis",
        "snowboard",
        "sports ball",
        "kite",
        "baseball bat",
        "baseball glove",
        "skateboard",
        "surfboard",
        "tennis racket",
        "bottle",
        "wine glass",
        "cup",
        "fork",
        "knife",
        "spoon",
        "bowl",
        "banana",
        "apple",
        "sandwich",
        "orange",
        "broccoli",
        "carrot",
        "hot dog",
        "pizza",
        "donut",
        "cake",
        "chair",
        "sofa",
        "pottedplant",
        "bed",
        "diningtable",
        "toilet",
        "tvmonitor",
        "laptop",
        "mouse",
        "remote",
        "keyboard",
        "cell phone",
        "microwave",
        "oven",
        "toaster",
        "sink",
        "refrigerator",
        "book",
        "clock",
        "vase",
        "scissors",
        "teddy bear",
        "hair drier",
        "toothbrush",
    };

    public RemoteYOLODetector(NNModel yoloModel) {
        _buffer = new RenderTexture(IMAGE_SIZE_WIDTH, IMAGE_SIZE_HEIGHT, 0);
        _pixels = new Texture2D(_buffer.width, _buffer.height);
        _requestId = 0;
        _requests = new Dictionary<uint, Request>();
        _results = new List<YLResult>();
        if (yoloModel != null) {
            _model = ModelLoader.Load(yoloModel);
            _worker = _model.CreateWorker();
        }
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
    public void Dispose() {
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
        if (_buffer != null) {
            UnityEngine.Object.Destroy(_buffer);
            _buffer = null;
        }
        if (_pixels != null) {
            UnityEngine.Object.Destroy(_pixels);
            _pixels = null;
        }
        if (_worker != null) {
            _worker.Dispose();
            _worker = null;
        }
        logit("Dispose");
    }

    // Sends the image to the queue and returns the request id;
    public uint DetectImage(Texture image) {
        Rect clipRect;
        if (image.width < image.height) {
            float ratio = (float)image.width/image.height;
            clipRect = new Rect(0, (1-ratio)/2, 1, ratio);
        } else {
            float ratio = (float)image.height/image.width;
            clipRect = new Rect((1-ratio)/2, 0, ratio, 1);
        }
        return DetectImage(image, clipRect);
    }

    public uint DetectImage(Texture image, Rect clipRect) {
        // Resize the texture.
        Graphics.Blit(image, _buffer, clipRect.size, clipRect.position);
        // Convert the texture.
        RenderTexture temp = RenderTexture.active;
        RenderTexture.active = _buffer;
        _pixels.ReadPixels(new Rect(0, 0, _buffer.width, _buffer.height), 0, 0);
        _pixels.Apply();
        RenderTexture.active = temp;
        _requestId++;
        switch (Mode) {
        case YLDetMode.ClientOnly:
            performLocalDetection(_requestId, _pixels, clipRect);
            break;
        case YLDetMode.ServerOnly:
            requestRemoteDetection(_requestId, _pixels, clipRect);
            break;
        default:
            addDummyResult(_requestId);
            break;
        }
        return _requestId;
    }

    // The number of pending requests.
    public int NumPendingRequests {
        get {
            return _requests.Count;
        }
    }

    private void performLocalDetection(uint requestId, Texture2D pixels, Rect clipRect) {
        if (_model == null) {
            Debug.LogWarning("performLocalDetection: model not loaded.");
            return;
        }

        DateTime t0 = DateTime.Now;
        var data = new TextureAsTensorData(pixels, 3);
        using (var tensor = new Tensor(data.shape, data)) {
            _worker.Execute(tensor);
        }

        List<YLObject> cands = new List<YLObject>();
        List<string> outputs = _model.outputs;
        Vector2[] anchors = (outputs.Count == 3)? ANCHORS_FULL : ANCHORS_TINY;
        for (int z = 0; z < outputs.Count; z++) {
            using (var t = _worker.PeekOutput(outputs[z])) {
                int rows = t.shape.height;
                int cols = t.shape.width;
                for (int y0 = 0; y0 < rows; y0++) {
                    for (int x0 = 0; x0 < cols; x0++) {
                        for (int k = 0; k < 3; k++) {
                            int b = (5+LABELS.Length-1) * k;
                            float conf = Sigmoid(t[0,y0,x0,b+4]);
                            if (conf < Threshold) continue;
                            Vector2 anchor = anchors[z*3+k];
                            float x = (x0 + Sigmoid(t[0,y0,x0,b+0])) / cols;
                            float y = (y0 + Sigmoid(t[0,y0,x0,b+1])) / rows;
                            float w = (anchor.x * Mathf.Exp(t[0,y0,x0,b+2])) / IMAGE_SIZE_WIDTH;
                            float h = (anchor.y * Mathf.Exp(t[0,y0,x0,b+3])) / IMAGE_SIZE_HEIGHT;
                            float maxProb = -1;
                            int maxIndex = 0;
                            for (int index = 1; index < LABELS.Length; index++) {
                                float p = t[0,y0,x0,b+5+index-1];
                                if (maxProb < p) {
                                    maxProb = p; maxIndex = index;
                                }
                            }
                            conf *= Sigmoid(maxProb);
                            if (conf < Threshold) continue;
                            YLObject obj1 = new YLObject {
                                Label = LABELS[maxIndex],
                                Conf = conf,
                                BBox = new Rect(
                                    clipRect.x+(x-w/2)*clipRect.width,
                                    clipRect.y+(y-h/2)*clipRect.height,
                                    w*clipRect.width,
                                    h*clipRect.height),
                            };
                            cands.Add(obj1);
                        }
                    }
                }
            }
        }

        // Apply Soft-NMS.
        List<YLObject> objs = new List<YLObject>();
        Dictionary<YLObject, float> cscore = new Dictionary<YLObject, float>();
        foreach (YLObject obj1 in cands) {
            cscore[obj1] = obj1.Conf;
        }
        while (0 < cands.Count) {
            // argmax(cscore[obj1])
            float mscore = -1;
            int mi = 0;
            for (int i = 0; i < cands.Count; i++) {
                float score = cscore[cands[i]];
                if (mscore < score) {
                    mscore = score; mi = i;
                }
            }
            if (mscore < Threshold) break;
            YLObject obj1 = cands[mi];
            objs.Add(obj1);
            cands.RemoveAt(mi);
            for (int i = 0; i < cands.Count; i++) {
                YLObject b1 = cands[i];
                float v = obj1.getIOU(b1);
                cscore[b1] *= Mathf.Exp(-3*v*v);
            }
        }

        DateTime t1 = DateTime.Now;
        YLResult result1 = new YLResult() {
            RequestId = requestId,
            SentTime = t0,
            RecvTime = t1,
            InferenceTime = (float)((t1 - t0).TotalSeconds),
            Objects = objs.ToArray(),
        };
        //logit("recvData: result1={0}", result1);
        _results.Add(result1);
    }

    private static byte[] JPEG_TYPE = { (byte)'J', (byte)'P', (byte)'E', (byte)'G' };
    private void requestRemoteDetection(uint requestId, Texture2D pixels, Rect clipRect) {
        if (_udp == null) {
            Debug.LogWarning("requestRemoteDetection: connection not open.");
            return;
        }

        Request request = new Request {
            RequestId = requestId,
            SentTime = DateTime.Now,
            ClipRect = clipRect
        };
        _requests.Add(requestId, request);
        byte[] data = pixels.EncodeToJPG();
        using (MemoryStream buf = new MemoryStream()) {
            writeBytes(buf, JPEG_TYPE);
            writeUInt32(buf, requestId);
            writeUInt32(buf, (uint)(Threshold*100));
            writeUInt32(buf, (uint)data.Length);
            writeBytes(buf, data);
            sendRTP(buf.ToArray());
        }
    }

    private void addDummyResult(uint requestId) {
        YLObject obj1 = new YLObject {
            Label = "cat",
            Conf = 1.0f,
            BBox = new Rect(0.5f, 0.5f, 0.4f, 0.4f),
        };
        YLResult result1 = new YLResult {
            RequestId = requestId,
            SentTime = DateTime.Now,
            RecvTime = DateTime.Now,
            InferenceTime = 0,
            Objects = new YLObject[] { obj1 },
        };
        _results.Add(result1);
    }

    // Gets the results (if any).
    public YLResult[] GetResults() {
        // Remove timeout keys from _requests.
        DateTime t = DateTime.Now;
        List<uint> removed = new List<uint>();
        foreach (Request req in _requests.Values) {
            if (REQUEST_TIMEOUT < (t - req.SentTime).TotalSeconds) {
                removed.Add(req.RequestId);
                logit("Timeout: "+req);
            }
        }
        foreach (uint requestId in removed) {
            _requests.Remove(requestId);
        }
        YLResult[] results = _results.ToArray();
        _results.Clear();
        return results;
    }

    private static void logit(string fmt, params object[] args) {
        Debug.Log(string.Format(fmt, args));
    }

    private float Sigmoid(float x) {
        return 1f/(1f+Mathf.Exp(-x));
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
            if (!_requests.ContainsKey(requestId)) return;
            Request request = _requests[requestId];
            _requests.Remove(requestId);
            Rect clipRect = request.ClipRect;
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
                    clipRect.x+(x/IMAGE_SIZE_WIDTH)*clipRect.width,
                    clipRect.y+(y/IMAGE_SIZE_HEIGHT)*clipRect.height,
                    (w/IMAGE_SIZE_WIDTH)*clipRect.width,
                    (h/IMAGE_SIZE_HEIGHT)*clipRect.height);
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
            _results.Add(result1);
        }
    }
}

} // namespace net.sss_consortium.fastdet
