//  DetectionTest.cs
//
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Barracuda;
using net.sss_consortium.fastdet;

public class DetectionTest : MonoBehaviour
{
    public RawImage rawImage = null;
    public string serverUrl = null;
    public NNModel yoloModel = null;
    public GUIStyle textStyle = new GUIStyle();
    public GUIStyle boxStyle = new GUIStyle();
    public Camera camera = null;
    public ARCameraManager cameraManager = null;

    public float DetectionInterval = 0.1f;
    public float DetectionThreshold = 0.3f;

    private WebCamTexture _webcam = null;
    private XRCameraSubsystem _xrcamera = null;
    private IObjectDetector _detector = null;
    private float _nextDetection = 0;
    private YLResult _result = null;
    private Texture2D _arcamTexture = null;

    void Start()
    {
        Debug.Log("AR Session: "+ARSession.state);

        _xrcamera = cameraManager.subsystem;
        if (_xrcamera != null) {
            Debug.Log("Using XR Camera.");
            cameraManager.frameReceived += cameraManager_frameReceived;
            rawImage.enabled = false;
        } else {
            Debug.Log("Using Webcam.");
            _webcam = new WebCamTexture();
            _webcam.Play();
            rawImage.enabled = true;
            rawImage.texture = _webcam;
        }

        setupNextDetector();
    }

    void OnDisable()
    {
        _webcam?.Stop();
        if (_xrcamera != null) {
            cameraManager.frameReceived -= cameraManager_frameReceived;
        }
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

        if (ARSession.state == ARSessionState.SessionTracking) {
            Transform transform = cameraManager.transform;
            string s = "AR Camera: "+transform.position;
            GUI.Label(new Rect(10,100,300,20), s, textStyle);
        }
    }

    void Update()
    {
        if (_detector != null) {
            Texture input = null;
            if (_arcamTexture != null) {
                input = _arcamTexture;
            } else if (_webcam != null) {
                input = _webcam;
            }
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

    private Vector3 getScreenPoint(float x, float y) {
        Vector3 p = new Vector3(Screen.width*x, Screen.height*y, 0);
        Ray ray = camera.ScreenPointToRay(p);
        RaycastHit hit;
        Physics.Raycast(ray, out hit);
        //Debug.Log("hit: "+hit.collider+", "+hit.point);
        return hit.point;
    }

    private void detector_ResultObtained(object sender, YLResultEventArgs e) {
        YLResult result = e.Result;
        if (_result == null || _result.SentTime < result.SentTime) {
            _result = result;
        }
    }

    private void cameraManager_frameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (!_xrcamera.TryAcquireLatestCpuImage(out XRCpuImage image)) return;

        var format = TextureFormat.RGBA32;
        if (_arcamTexture == null ||
            _arcamTexture.width != image.width ||
            _arcamTexture.height != image.height) {
            _arcamTexture = new Texture2D(image.width, image.height, format, false);
        }

        //XRCpuImage.Transformation transformation = XRCpuImage.Transformation.MirrorY;
        XRCpuImage.Transformation transformation = XRCpuImage.Transformation.MirrorX;
        var conversionParams = new XRCpuImage.ConversionParams(image, format, transformation);

        var rawTextureData = _arcamTexture.GetRawTextureData<byte>();
        try {
            image.Convert(conversionParams, rawTextureData);
        } finally {
            image.Dispose();
        }
        _arcamTexture.Apply();
    }

}
