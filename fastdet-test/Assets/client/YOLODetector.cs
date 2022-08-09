///  YOLODetector.cs
///
using System;
using System.Collections.Generic;
using UnityEngine;

namespace net.sss_consortium.fastdet {

public abstract class YOLODetector : IObjectDetector {

    private RenderTexture _buffer;
    private Texture2D _pixels;
    private uint _requestId;
    private Dictionary<uint, YLRequest> _requests;
    private List<YLResult> _results;
    private string[] _labels;

    private const float REQUEST_TIMEOUT = 3f;

    private const int IMAGE_SIZE_WIDTH = 416;
    private const int IMAGE_SIZE_HEIGHT = 416;

    public static string[] RSU_LABELS = {
        // RSU dataset labels.
        null,                   // UNDEFINED
        "person",
        "car",
        "bicycle",
        "camera",
        "a60g",
        "rsubox",
        "asub6",
        "ammw",
        "autocar",
    };

    public static string[] COCO_LABELS = {
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

    public YOLODetector(string[] labels) {
        _buffer = new RenderTexture(IMAGE_SIZE_WIDTH, IMAGE_SIZE_HEIGHT, 0);
        _pixels = new Texture2D(_buffer.width, _buffer.height);
        _requestId = 0;
        _requests = new Dictionary<uint, YLRequest>();
        _results = new List<YLResult>();
        _labels = labels;
    }

    // Uninitializes the endpoint connection.
    public virtual void Dispose() {
        logit("Dispose");
        if (_buffer != null) {
            UnityEngine.Object.Destroy(_buffer);
            _buffer = null;
        }
        if (_pixels != null) {
            UnityEngine.Object.Destroy(_pixels);
            _pixels = null;
        }
    }

    // Sends the image to the queue and returns the request id;
    public YLRequest ProcessImage(Texture image, Rect detectArea, float threshold) {
        // Resize the texture.
        Graphics.Blit(image, _buffer, detectArea.size, detectArea.position);
        // Convert the texture.
        RenderTexture temp = RenderTexture.active;
        RenderTexture.active = _buffer;
        _pixels.ReadPixels(new Rect(0, 0, _buffer.width, _buffer.height), 0, 0);
        _pixels.Apply();
        RenderTexture.active = temp;
        // Create a Request.
        _requestId++;
        YLRequest request = new YLRequest {
            RequestId = _requestId,
            SentTime = DateTime.Now,
            ImageSize = new Vector2(_pixels.width, _pixels.height),
            DetectArea = detectArea,
            Threshold = threshold,
        };
        performDetection(request, _pixels);
        return request;
    }

    protected abstract void performDetection(YLRequest request, Texture2D pixels);

    protected void addRequest(YLRequest request) {
        _requests.Add(request.RequestId, request);
    }

    protected YLRequest removeRequest(uint requestId) {
        if (!_requests.ContainsKey(requestId)) return null;
        YLRequest request = _requests[requestId];
        _requests.Remove(requestId);
        return request;
    }

    protected void addResult(YLResult result) {
        _results.Add(result);
    }

    public string[] Labels {
        get {
            return _labels;
        }
    }

    // The number of pending requests.
    public int NumPendingRequests {
        get {
            return _requests.Count;
        }
    }

    public event EventHandler<YLResultEventArgs> ResultObtained;

    protected virtual void OnResultObtained(YLResultEventArgs e) {
        if (ResultObtained != null) {
            ResultObtained(this, e);
        }
    }

    public event EventHandler<YLRequestEventArgs> RequestTimeout;

    protected virtual void OnRequestTimedout(YLRequestEventArgs e) {
        if (RequestTimeout != null) {
            RequestTimeout(this, e);
        }
    }

    // Update the tasks.
    public void Update() {
        // Remove timeout keys from _requests.
        DateTime t = DateTime.Now;
        List<uint> removed = new List<uint>();
        foreach (YLRequest req in _requests.Values) {
            if (REQUEST_TIMEOUT < (t - req.SentTime).TotalSeconds) {
                removed.Add(req.RequestId);
                OnRequestTimedout(new YLRequestEventArgs(req));
                logit("Timeout: "+req);
            }
        }
        foreach (uint requestId in removed) {
            _requests.Remove(requestId);
        }
        foreach (YLResult result in _results) {
            OnResultObtained(new YLResultEventArgs(result));
        }
        _results.Clear();
    }

    protected static void logit(string fmt, params object[] args) {
        Debug.Log(string.Format(fmt, args));
    }

}

} // namespace net.sss_consortium.fastdet
