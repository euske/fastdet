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
public class YLRequest {

    public uint RequestId;      // Request ID.
    public DateTime SentTime;   // Timestamp (sent).
    public Vector2 ImageSize;   // Input image size.
    public Rect DetectArea;     // Detection area.
    public float Threshold;     // Detection threshold.

    public override string ToString() {
        return string.Format(
            "<YLRequest: RequstId={0}, SentTime={1}, ImageSize={2}, DetectArea={3}, Threshold={4}>",
            RequestId, SentTime, ImageSize, DetectArea, Threshold);
    }
};

//  YLResult
//
public class YLResult {

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

//  YLRequestEventArgs
//
public class YLRequestEventArgs : EventArgs {

    public YLRequest Request { get; set; }

    public YLRequestEventArgs(YLRequest request) {
        Request = request;
    }
}

//  YLResultEventArgs
//
public class YLResultEventArgs : EventArgs {

    public YLResult Result { get; set; }

    public YLResultEventArgs(YLResult result) {
        Result = result;
    }
}

//  IObjectDetector
//
//  void Start() {
//    detector = new RemoteYOLODetector("rtsp://192.168.1.1:1234/detect");
//    detector.ResultObtained += resultObtained;
//  }
//
//  void Update() {
//    var image = ...;
//    var request = detector.ProcessImage(image, threshold);
//    detector.Update();
//  }
//  void resultObtained(object sender, YLResultEventArgs e) {
//    var result = e.Result;
//    ...
//  }
//
interface IObjectDetector : IDisposable {

    // Sends the image to the queue and returns the request id;
    YLRequest ProcessImage(Texture image, Rect detectArea, float threshold);

    // Updates the tasks and fires the events.
    void Update();

    // The number of pending requests.
    int NumPendingRequests { get; }

    // Fired when a result is obtained.
    event EventHandler<YLResultEventArgs> ResultObtained;
    // Fired when a request is timed out.
    event EventHandler<YLRequestEventArgs> RequestTimeout;
}

} // namespace net.sss_consortium.fastdet
