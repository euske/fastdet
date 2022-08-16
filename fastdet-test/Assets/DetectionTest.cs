//  DetectionTest.cs
//
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Barracuda;
using net.sss_consortium.fastdet;

public class DetectionTest : MonoBehaviour
{
    public RawImage rawImage = null;
    public string serverUrlCOCO = null;
    public string serverUrlRSU = null;
    public NNModel yoloModel = null;
    public GUIStyle textStyle = new GUIStyle();
    public GUIStyle boxStyle = new GUIStyle();

    public float DetectionInterval = 0.1f;
    public float DetectionThreshold = 0.05f;

    private WebCamTexture _webcam = null;
    private IObjectDetector _detector = null;
    private float _nextDetection = 0;
    private YLResult _result = null;

    private string _curMode = null;
    private string[] MODES = { "dummy", "local", "coco", "rsu" };

    void Start()
    {
        Debug.Log("Using Webcam.");
        _webcam = new WebCamTexture();
        _webcam.Play();
        rawImage.enabled = true;
        rawImage.texture = _webcam;

        setupNextDetector();
    }

    void OnDisable()
    {
        _webcam?.Stop();
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
                GUI.Box(rect, obj1.Label, boxStyle);
            }
        }
        if (_curMode != null) {
            if (GUI.Button(new Rect(width-200,20,160,60), _curMode.ToString())) {
                setupNextDetector();
                _result = null;
            }
        }
    }

    void Update()
    {
        if (_detector != null) {
            Texture input = _webcam;
            if (input != null && 16 <= input.width && 16 <= input.height) {
                if (_nextDetection < Time.time) {
                    Rect area;
                    if (input.width < input.height) {
                        float ratio = (float)input.width/input.height;
                        area = new Rect(0, (1-ratio)/2, 1, ratio);
                    } else {
                        float ratio = (float)input.height/input.width;
                        area = new Rect((1-ratio)/2, 0, ratio, 1);
                    }
                    _detector.ProcessImage(input, area, DetectionThreshold);
                    _nextDetection = Time.time + DetectionInterval;
                    Debug.Log("nextDetection:"+_nextDetection);
                }
            }
            _detector.Update();
        }
    }

    private void setupNextDetector() {
        _detector?.Dispose();
        _detector = null;

        string mode = _curMode;
        switch (mode) {
        case "dummy":
            mode = "local";
            break;
        case "local":
            mode = "coco";
            break;
        case "coco":
            mode = "rsu";
            break;
        default:
            mode = "dummy";
            break;
        }
        _curMode = mode;

        switch (mode) {
        case "local":
            if (yoloModel != null) {
                _detector = new LocalYOLODetector(yoloModel, YOLODetector.COCO_LABELS);
            }
            break;
        case "coco":
            if (serverUrlCOCO != null) {
                try {
                    _detector = new RemoteYOLODetector(serverUrlCOCO, YOLODetector.COCO_LABELS);
                } catch (Exception e) {
                    Debug.LogWarning("connection error: "+e);
                }
            }
            break;
        case "rsu":
            if (serverUrlRSU != null) {
                try {
                    _detector = new RemoteYOLODetector(serverUrlRSU, YOLODetector.RSU_LABELS);
                } catch (Exception e) {
                    Debug.LogWarning("connection error: "+e);
                }
            }
            break;
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
