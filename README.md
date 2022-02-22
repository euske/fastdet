# FastDet

Fast object detector with distributed neural network.

Namespace: net.sss_consortium.fastdet

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
    uint Timestamp0;            // Timestamp (local).
    uint Timestamp1;            // Timestamp (remote).
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
interface IObjectDetector {

    // Detection mode.
    string Mode { get; set; }
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
```
