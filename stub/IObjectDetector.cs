///  IObjectDetector.cs
///
using System;

namespace net.sss_consortium.fastdet {

//  YLObject
//
public struct YLObject {
    public string Label;               // Object label.
    public float Conf;                 // Confidence.
    public Rect BBox;                  // Bounding Box.
}

//  YLResult
//
public struct YLResult {
    public uint RequestId;             // Request ID.
    public DateTime SentTime;          // Timestamp (sent).
    public DateTime RecvTime;          // Timestamp (received).
    public YLObject[] Objects;         // List of detected objects.
}

//  YLDetMode
//
public enum YLDetMode {
    None,        // Just return dummy data.
    ClientOnly,  // YOLO-tiny at client, no networking.
    ServerOnly,  // YOLO-full at server, full image transfer.
    //Mixed,     // YOLO-full at client/server, with autoencoder.
}

//  IObjectDetector
//
//  void Start() {
//    detector = new RemoteYOLODetector();
//    detector.Open("rtsp://192.168.1.1:1234/detect");
//    //detector.Mode = ServerOnly;
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
interface IObjectDetector {

    // Detection mode.
    YLDetMode Mode { get; set; }
    // Detection threshold.
    float Threshold { get; set; }

    // Initializes the endpoint connection.
    void Open(string url);
    // Uninitializes the endpoint connection.
    void Close();

    // Sends the image to the queue and returns the request id;
    uint DetectImage(Texture image);
    // Gets the results (if any).
    YLResult[] GetResults();
}

} // namespace net.sss_consortium.fastdet
