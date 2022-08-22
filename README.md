# FastDet

Fast object detector with distributed neural network.

## Prerequisites

### Client

 - Unity
 - Barracuda (> 2.0.0)

### Server

 - Python
 - Pillow
 - ONNX Runtime (w/ GPU)

    $ pip install -r requirements.txt

## Testing

### Test detector only

    $ python server/detector.py -c 80 models/yolov3-full.onnx testdata/dog.jpg
    $ python server/detector.py -c 9 models/yolov3-rsu.onnx testdata/rsu1.jpg

### Test server with dummy detector

    $ python server/server.py -s 10000
    $ python server/client.py rtsp://localhost:10000/detect testdata/dog.jpg

### Test server with full detector

    $ python server/server.py -s 10000 full:80:models/yolov3-full.onnx
    $ python server/client.py rtsp://localhost:10000/full testdata/dog.jpg

### Test server w/ CUDA

    $ python server/server.py -s 10000 -m cuda full:80:models/yolov3-full.onnx

### Debugging on Android

    > cd \Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools
    > adb logcat -c
    > adb logcat -s Unity

## Running

 1. launch the server.
 2. open the SampleScene.unity.
 3. configure the Server Url with the appropriate host/port.
 4. play the scene.


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
