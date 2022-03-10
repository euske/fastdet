//  DetectionTest.cs
//
using UnityEngine;
using UnityEngine.UI;
using net.sss_consortium.fastdet;

public class DetectionTest : MonoBehaviour
{
    public RawImage rawImage = null;
    public string serverUrl = null;

    private WebCamTexture _webcam;
    private IObjectDetector _detector;

    void Start()
    {
        _webcam = new WebCamTexture();
        _webcam.Play();
        _detector = new RemoteYOLODetector();
        if (serverUrl != null) {
            _detector.Open(serverUrl);
            _detector.Mode = YLDetMode.ServerOnly;
        }

        rawImage.texture = _webcam;
    }

    void OnDisable()
    {
        _webcam.Stop();
    }

    void OnDestroy()
    {
        if (_webcam != null) Destroy(_webcam);
        _detector?.Dispose();
        _detector = null;
    }

    void Update()
    {
        if (16 <= _webcam.width && 16 <= _webcam.height) {
            _detector.DetectImage(_webcam);
        }
    }
}
