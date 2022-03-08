//  DetectionTest.cs
//
using UnityEngine;
using UnityEngine.UI;
using net.sss_consortium.fastdet;

public class DetectionTest : MonoBehaviour
{
    public RawImage rawImage = null;

    private WebCamTexture _webcam;
    private IObjectDetector _detector;

    void Start()
    {
        _webcam = new WebCamTexture();
        _webcam.Play();
        _detector = new DummyDetector();

        rawImage.texture = _webcam;
    }

    void onDisable()
    {
        _webcam.Stop();
    }

    void Update()
    {
        if (16 <= _webcam.width && 16 <= _webcam.height) {
            _detector.DetectImage(_webcam);
        }
    }
}
