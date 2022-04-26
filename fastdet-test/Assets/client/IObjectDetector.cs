///  IObjectDetector.cs
///
using System;
using UnityEngine;

namespace net.sss_consortium.fastdet {

//  YLObject
//
public struct YLObject {
    public string Label;               // Object label.
    public float Conf;                 // Confidence.
    public Rect BBox;                  // Bounding Box.

    public override string ToString() {
        return string.Format(
            "<YLObject: Label={0}, Conf={1}, Rect={2}",
            Label, Conf, BBox);
    }

    public float getIOU(YLObject obj) {
        Rect bbox0 = this.BBox;
        Rect bbox1 = obj.BBox;
        float x = Mathf.Max(bbox0.x, bbox1.x);
        float y = Mathf.Max(bbox0.y, bbox1.y);
        float w = Mathf.Min(bbox0.x+bbox0.width, bbox1.x+bbox1.width) - x;
        float h = Mathf.Min(bbox0.y+bbox0.height, bbox1.y+bbox1.height) - y;
        return (w*h)/(bbox0.width*bbox0.height);
    }
}

//  YLRequest
//
public struct YLRequest {
    public uint RequestId;      // Request ID.
    public DateTime SentTime;   // Timestamp (sent).
    public Rect ClipRect;       // Clip rectangle.

    public override string ToString() {
        return string.Format(
            "<YLRequest: RequstId={0}, SentTime={1}, ClipRect={2}>",
            RequestId, SentTime, ClipRect);
    }
};

//  YLResult
//
public struct YLResult {
    public uint RequestId;             // Request ID.
    public DateTime SentTime;          // Timestamp (sent).
    public DateTime RecvTime;          // Timestamp (received).
    public float InferenceTime;        // Inference time (in second).
    public YLObject[] Objects;         // List of detected objects.

    public override string ToString() {
        return string.Format(
            "<YLResult: RequestId={0}, SentTime={1}, RecvTime={2}, InferenceTime={3}, Objects={4}",
            RequestId, SentTime, RecvTime, InferenceTime, string.Join(", ", Objects));
    }
}

//  YLDetMode
//
public enum YLDetMode {
    None,        // Just return dummy data.
    ClientOnly,  // YOLO-tiny at client, no networking.
    ServerOnly,  // YOLO-full at server, full image transfer.
    //Mixed,     // YOLO-full at client/server, with autoencoder.
}

//  YLRequestEventArgs
//
public class YLRequestEventArgs : EventArgs {
    public YLRequest Request { get; set; }
}

//  YLResultEventArgs
//
public class YLResultEventArgs : EventArgs {
    public YLResult Result { get; set; }
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
    int NumPendingRequests { get; }
}

} // namespace net.sss_consortium.fastdet
