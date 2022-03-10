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
    private YLResult? _result = null;

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

    void OnGUI()
    {
        if (_result != null) {
            int width = Screen.width;
            int height = Screen.height;
            foreach (YLObject obj1 in _result.Value.Objects) {
                Rect rect = new Rect(
                    obj1.BBox.x*width,
                    obj1.BBox.y*height,
                    obj1.BBox.width*width,
                    obj1.BBox.height*height);
                Debug.Log("GUI:"+rect);
                GUI.Box(rect, obj1.Label);
            }
        }
    }

    void Update()
    {
        if (16 <= _webcam.width && 16 <= _webcam.height) {
            _detector.DetectImage(_webcam);
        }
        YLResult[] results = _detector.GetResults();
        if (0 < results.Length) {
            _result = results[0];
        }
    }
}
