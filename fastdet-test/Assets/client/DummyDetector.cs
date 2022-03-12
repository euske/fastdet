///  DummyDetector.cs
///
using System;
using UnityEngine;

namespace net.sss_consortium.fastdet {

public class DummyDetector : IObjectDetector {

    // Detection mode.
    public YLDetMode Mode { get; set; }
    // Detection threshold.
    public float Threshold { get; set; }

    // Initializes the endpoint connection.
    public void Open(string url) {
    }

    // Uninitializes the endpoint connection.
    public void Dispose() {
    }

    // Sends the image to the queue and returns the request id;
    public uint DetectImage(Texture image) {
        return 1;
    }

    // The number of pending requests.
    public int NumPendingRequests {
        get {
            return 0;
        }
    }

    // Gets the results (if any).
    public YLResult[] GetResults() {
        YLObject obj1 = new YLObject();
        obj1.Label = "cat";
        obj1.Conf = 1.0f;
        obj1.BBox = new Rect(10, 10, 100, 100);
        YLResult result1 = new YLResult();
        result1.RequestId = 1;
        result1.SentTime = DateTime.Now;
        result1.RecvTime = DateTime.Now;
        result1.InferenceTime = 0;
        result1.Objects = new YLObject[] { obj1 };
        return new YLResult[] { result1 };
    }
}

} // namespace net.sss_consortium.fastdet
