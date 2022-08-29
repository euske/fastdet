# Design Docs


## Protocols

Fastdet uses a "RTSP-like" (not real RTSP) protocol for sending images.
By "-like", I mean that the role of client and server is reversed in that
the original RTSP is a server sending video feeds to a client whereas
this protocol allows a client to send video feeds to a server, which
performs object detection. (This client-to-server feed was mentioned
in RTSP 1.0 but its specification was never materialized, and they're
dropped in RTSP 2.0.)

References:

 * RFC 2326: https://www.rfc-editor.org/rfc/rfc2326
 * RFC 1889: https://www.rfc-editor.org/rfc/rfc1889

### URI

  rtsp://[host][:port]/[path]

### Establishing Connection

  All character encodings are in UTF-8.

  1. Client -> Server: makes a tcp connection.
  2. Server -> Client: accepts.
  3. Client -> Server: sends `FEED [lport] [path]`
  4. Server -> Client: sends `+OK [rport] [sessionId]`
     (Session ID is actually never used.)
  5. Set the sequence number to 1 on both sides.

### Sending Images

  1. Client -> Server: sends a 12-byte 'emtpy' RTP packet.
  2. Server -> Client: sends a 12-byte 'emtpy' RTP packet.
  3. Client -> Server: sends a request.

     0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |V=2|P|X|  CC   |M|     PT      |       sequence number         |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |      'J'      |      'P'      |      'E'      |      'G'      |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    request id (uint32_t)                      |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    threshold * 100 (uint32_t)                 |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    data length (uint32_t)                     |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    MJPEG frame                                |
    |                    ...                                        |

  4. Server -> Client: performs detection and sends a response.

     0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |V=2|P|X|  CC   |M|     PT      |       sequence number         |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |      'Y'      |      'O'      |      'L'      |      'O'      |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    request id (uint32_t)                      |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    recognition time (in msec, uint32_t)       |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    data length (uint32_t)                     |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    Detection result                           |
    |                    ...                                        |

### Detection Result

    struct result {
        uint8_t object_class;     // 0: person, 1: car, ...
        uint8_t confidence_class; // 255: highest;
        int16_t x;
        int16_t y;
        int16_t width;
        int16_t height;
    };


## API

Namespace: `net.sss_consortium.fastdet`

```
//  YLObject
//
struct YLObject {
    string Label;               // Object label.
    float Conf;                 // Confidence.
    Rect BBox;                  // Bounding Box.
}

//  YLResult
//
struct YLResult {
    uint RequestId;             // Request ID.
    DateTime SentTime;          // Timestamp (sent).
    DateTime RecvTime;          // Timestamp (received).
    float InferenceTime;        // Inference time (in second).
    YLObject[] Objects;         // List of detected objects.
}

//  IObjectDetector
//
//  void Start() {
//    detector = new RemoteYOLODetector();
//    detector.Open("rtsp://192.168.1.1:1234/detect");
//    //detector.Mode = "test1";
//  }
//
//  void Update() {
//    var image = ...;
//    var reqid = detector.DetectImage(image);
//    foreach (YLResult result : detector.GetResults()) {
//        ...
//    }
//  }
//
interface IObjectDetector : IDisposable {

    // Detection mode.
    YLDetMode Mode { get; set; }
    // Detection threshold.
    float Threshold { get; set; }

    // Initializes the endpoint connection.
    void Open(string url);

    // Sends the image to the queue and returns the request id;
    uint DetectImage(Texture image);
    // Gets the results (if any).
    YLResult[] GetResults();

    // The number of pending requests.
    public int NumPendingRequests { get; }
}
```
