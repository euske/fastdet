///  DummyDetector.cs
///

public class DummyDetector : IObjectDetector {

    // Detection mode.
    public string Mode { get; set; }
    // Detection threshold.
    public float Threshold { get; set; }

    // Initializes the endpoint connection.
    public void Open(string url) {
    }

    // Uninitializes the endpoint connection.
    public void Close() {
    }

    // Sends the image to the queue and returns the request id;
    public uint DetectImage(Texture image) {
        return 1;
    }

    // Gets the results (if any).
    public YLResult[] GetResults() {
        YLObject obj1 = new YLObject();
        obj1.Label = "cat";
        obj1.Conf = 1.0f;
        obj1.BBox = new Rect(10, 10, 100, 100);
        YLResult result1 = new YLResult();
        result1.RequestId = 1;
        result1.Timestamp0 = 0;
        result1.Timestamp1 = 0;
        result1.Objects = new YLObject[] { obj1 };
        return new YLResult[] { result1 };
    }
}
