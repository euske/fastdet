//  DetectionTest.cs
//
using System;
using UnityEngine;
using UnityEngine.UI;
using Unity.Barracuda;
using net.sss_consortium.fastdet;

public class DetectionTest : MonoBehaviour
{
    public RawImage rawImage = null;
    public string serverUrl = null;
    public NNModel yoloModel = null;

    private WebCamTexture _webcam;
    private IObjectDetector _detector;
    private YLResult? _result = null;

    void Start()
    {
        _webcam = new WebCamTexture();
        _webcam.Play();
        rawImage.texture = _webcam;

        _detector = new RemoteYOLODetector(yoloModel);
        if (yoloModel != null) {
            _detector.Mode = YLDetMode.ClientOnly;
        }
        if (serverUrl != null) {
            try {
                _detector.Open(serverUrl);
                _detector.Mode = YLDetMode.ServerOnly;
            } catch (Exception e) {
                Debug.LogWarning("connection error: "+e);
            }
        }
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
        int width = Screen.width;
        int height = Screen.height;
        if (_result != null) {
            YLResult result = _result.Value;
            int total = (int)((result.RecvTime-result.SentTime).TotalSeconds*1000);
            int infer = (int)(result.InferenceTime*1000);
            string text = "Total: "+total+"ms, Inference: "+infer+"ms";
            GUI.Label(new Rect(10,10,300,20), text);
            foreach (YLObject obj1 in result.Objects) {
                Rect rect = new Rect(
                    obj1.BBox.x*width,
                    obj1.BBox.y*height,
                    obj1.BBox.width*width,
                    obj1.BBox.height*height);
                Debug.Log("GUI:"+rect);
                GUI.Box(rect, obj1.Label);
            }
        }
        if (_detector != null) {
            if (GUI.Button(new Rect(width-100,10,80,30), _detector.Mode.ToString())) {
                switch (_detector.Mode) {
                case YLDetMode.ClientOnly:
                    _detector.Mode = YLDetMode.ServerOnly;
                    break;
                case YLDetMode.ServerOnly:
                    _detector.Mode = YLDetMode.None;
                    break;
                default:
                    _detector.Mode = YLDetMode.ClientOnly;
                    break;
                }
            }
        }
    }

    void Update()
    {
        if (16 <= _webcam.width && 16 <= _webcam.height) {
            _detector.DetectImage(_webcam);
        }
        foreach (YLResult result in _detector.GetResults()) {
            _result = result;
        }
    }
}
