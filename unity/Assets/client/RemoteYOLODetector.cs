///  RemoteYOLODetector.cs
///
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;

namespace net.sss_consortium.fastdet {

public class RemoteYOLODetector : IObjectDetector {

    // Detection mode.
    public YLDetMode Mode { get; set; }
    // Detection threshold.
    public float Threshold { get; set; }

    private RenderTexture _scaleBuffer;
    private Texture2D _buffer;

    private uint _requestId = 0;
    private List<YLResult> _results = new List<YLResult>();

    public RemoteYOLODetector() {
        _scaleBuffer = new RenderTexture(416, 416, 0);
        _buffer = new Texture2D(_scaleBuffer.width, _scaleBuffer.height);
    }

    // Initializes the endpoint connection.
    public void Open(string url) {
        Debug.Log("Open: "+url);
    }

    // Uninitializes the endpoint connection.
    public void Dispose() {
        if (_scaleBuffer != null) UnityEngine.Object.Destroy(_scaleBuffer);
        if (_buffer != null) UnityEngine.Object.Destroy(_buffer);
        Debug.Log("Dispose");
    }

    // Sends the image to the queue and returns the request id;
    public uint DetectImage(Texture image) {
        _requestId++;
        // Resize the texture.
        var aspect = (float)image.height / image.width;
        var scale = new Vector2(aspect, 1);
        var offset = new Vector2((1 - aspect) / 2, 0);
        Graphics.Blit(image, _scaleBuffer, scale, offset);
        // Convert the texture.
        RenderTexture.active = _scaleBuffer;
        _buffer.ReadPixels(new Rect(0, 0, _buffer.width, _buffer.height), 0, 0);
        _buffer.Apply();
        switch (Mode) {
        case YLDetMode.ClientOnly:
            performLocalDetection(_requestId);
            break;
        case YLDetMode.ServerOnly:
            requestRemoteDetection(_requestId);
            break;
        default:
            addDummyResult(_requestId);
            break;
        }
        return _requestId;
    }

    private void performLocalDetection(uint requestId) {
        var data = new TextureAsTensorData(_buffer, 3);
    }

    private void requestRemoteDetection(uint requestId) {
        byte[] data = _buffer.EncodeToJPG();
    }

    private void addDummyResult(uint requestId) {
        YLObject obj1 = new YLObject();
        obj1.Label = "cat";
        obj1.Conf = 1.0f;
        obj1.BBox = new Rect(10, 10, 100, 100);
        YLResult result1 = new YLResult();
        result1.RequestId = requestId;
        result1.SentTime = DateTime.Now;
        result1.RecvTime = DateTime.Now;
        result1.InferenceTime = 0;
        result1.Objects = new YLObject[] { obj1 };
        _results.Add(result1);
    }

    // Gets the results (if any).
    public YLResult[] GetResults() {
        YLResult[] results = _results.ToArray();
        _results.Clear();
        return results;
    }
}

} // namespace net.sss_consortium.fastdet
