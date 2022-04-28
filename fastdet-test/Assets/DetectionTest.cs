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
    public GUIStyle textStyle = new GUIStyle();
    public GUIStyle boxStyle = new GUIStyle();

    public float DetectionInterval = 0.1f;
    public float DetectionThreshold = 0.3f;

    private WebCamTexture _webcam = null;
    private IObjectDetector _detector = null;
    private float _nextDetection = 0;
    private YLResult _result = null;

    void Start()
    {
        _webcam = new WebCamTexture();
        _webcam.Play();
        rawImage.texture = _webcam;

        setupNextDetector();
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
            int total = (int)((_result.RecvTime-_result.SentTime).TotalSeconds*1000);
            int infer = (int)(_result.InferenceTime*1000);
            string text = "Total: "+total+"ms, Inference: "+infer+"ms";
            GUI.Label(new Rect(10,10,300,20), text, textStyle);
            foreach (YLObject obj1 in _result.Objects) {
                Rect rect = new Rect(
                    obj1.BBox.x*width,
                    obj1.BBox.y*height,
                    obj1.BBox.width*width,
                    obj1.BBox.height*height);
                Debug.Log("GUI:"+rect);
                GUI.Box(rect, obj1.Label, boxStyle);
            }
        }
        if (_detector != null) {
            string mode = "dummy";
            if (_detector is RemoteYOLODetector) {
                mode = "remote";
            } else if (_detector is LocalYOLODetector) {
                mode = "local";
            }
            if (GUI.Button(new Rect(width-200,20,160,60), mode.ToString())) {
                setupNextDetector();
                _result = null;
            }
        }
    }

    void Update()
    {
        if (_detector != null) {
            if (16 <= _webcam.width && 16 <= _webcam.height) {
                if (_nextDetection < Time.time) {
                    _detector.ProcessImage(_webcam, DetectionThreshold);
                    _nextDetection = Time.time + DetectionInterval;
                }
            }
            _detector.Update();
        }
    }

    private void setupNextDetector() {
        IObjectDetector prev = _detector;
        _detector?.Dispose();
        _detector = null;

        if (prev == null || prev is DummyDetector) {
            if (serverUrl != null) {
                try {
                    _detector = new RemoteYOLODetector(serverUrl);
                } catch (Exception e) {
                    Debug.LogWarning("connection error: "+e);
                }
            }
        }
        if (_detector == null && !(prev is LocalYOLODetector)) {
            if (yoloModel != null) {
                _detector = new LocalYOLODetector(yoloModel);
            }
        }
        if (_detector == null) {
            _detector = new DummyDetector();
        }
        _detector.ResultObtained += detector_ResultObtained;
        Debug.Log("setupNextDetector: "+_detector);
    }

    private void detector_ResultObtained(object sender, YLResultEventArgs e) {
        YLResult result = e.Result;
        if (_result == null || _result.SentTime < result.SentTime) {
            _result = result;
        }
    }
}
