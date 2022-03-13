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
        YLObject obj1 = new YLObject {
            Label = "cat",
            Conf = 1.0f,
            BBox = new Rect(0.5f, 0.5f, 0.4f, 0.4f),
        };
        YLResult result1 = new YLResult {
            RequestId = 1,
            SentTime = DateTime.Now,
            RecvTime = DateTime.Now,
            InferenceTime = 0,
            Objects = new YLObject[] { obj1 },
        };
        return new YLResult[] { result1 };
    }
}

} // namespace net.sss_consortium.fastdet
