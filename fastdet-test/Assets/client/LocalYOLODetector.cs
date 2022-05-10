///  LocalYOLODetector.cs
///
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;

namespace net.sss_consortium.fastdet {

public class LocalYOLODetector : YOLODetector {

    private Model _model;
    private IWorker _worker;

    private static WorkerFactory.Type WORKER_TYPE = WorkerFactory.Type.CSharpBurst;

    private static Vector2[] ANCHORS_FULL = new Vector2[] {
        new Vector2(116, 90),
        new Vector2(156, 198),
        new Vector2(373, 326),
        new Vector2(30, 61),
        new Vector2(62, 45),
        new Vector2(59, 119),
        new Vector2(10, 13),
        new Vector2(16, 30),
        new Vector2(33, 23),
    };
    private static Vector2[] ANCHORS_TINY = new Vector2[] {
        new Vector2(81, 82),
        new Vector2(135, 169),
        new Vector2(344, 319),
        new Vector2(10, 14),
        new Vector2(23, 27),
        new Vector2(37, 58),
    };

    public LocalYOLODetector(NNModel yoloModel) {
        _model = ModelLoader.Load(yoloModel);
        _worker = WorkerFactory.CreateWorker(WORKER_TYPE, _model);
    }

    // Uninitializes the endpoint connection.
    public override void Dispose() {
        base.Dispose();
        _worker?.Dispose();
        _worker = null;
    }

    protected override void performDetection(YLRequest request, Texture2D pixels) {
        if (_model == null) {
            Debug.LogWarning("performDetection: model not loaded.");
            return;
        }

        DateTime t0 = DateTime.Now;
        using (var data = new TextureAsTensorData(pixels, 3)) {
            using (var tensor = new Tensor(data.shape, data)) {
                _worker.Execute(tensor);
            }
        }

        Rect detectArea = request.DetectArea;
        List<YLObject> cands = new List<YLObject>();
        List<string> outputs = _model.outputs;
        Vector2[] anchors = (outputs.Count == 3)? ANCHORS_FULL : ANCHORS_TINY;
        for (int z = 0; z < outputs.Count; z++) {
            using (var t = _worker.PeekOutput(outputs[z])) {
                int rows = t.shape.height;
                int cols = t.shape.width;
                for (int y0 = 0; y0 < rows; y0++) {
                    for (int x0 = 0; x0 < cols; x0++) {
                        for (int k = 0; k < 3; k++) {
                            int b = (5+LABELS.Length-1) * k;
                            float conf = Sigmoid(t[0,y0,x0,b+4]);
                            if (conf < request.Threshold) continue;
                            Vector2 anchor = anchors[z*3+k];
                            float x = (x0 + Sigmoid(t[0,y0,x0,b+0])) / cols;
                            float y = (y0 + Sigmoid(t[0,y0,x0,b+1])) / rows;
                            float w = (anchor.x * Mathf.Exp(t[0,y0,x0,b+2])) / pixels.width;
                            float h = (anchor.y * Mathf.Exp(t[0,y0,x0,b+3])) / pixels.height;
                            float maxProb = -1;
                            int maxIndex = 0;
                            for (int index = 1; index < LABELS.Length; index++) {
                                float p = t[0,y0,x0,b+5+index-1];
                                if (maxProb < p) {
                                    maxProb = p; maxIndex = index;
                                }
                            }
                            conf *= Sigmoid(maxProb);
                            if (conf < request.Threshold) continue;
                            YLObject obj1 = new YLObject {
                                Label = LABELS[maxIndex],
                                Conf = conf,
                                BBox = new Rect(
                                    detectArea.x+(x-w/2)*detectArea.width,
                                    detectArea.y+(y-h/2)*detectArea.height,
                                    w*detectArea.width,
                                    h*detectArea.height),
                            };
                            cands.Add(obj1);
                        }
                    }
                }
            }
        }

        // Apply Soft-NMS.
        List<YLObject> objs = new List<YLObject>();
        Dictionary<YLObject, float> cscore = new Dictionary<YLObject, float>();
        foreach (YLObject obj1 in cands) {
            cscore[obj1] = obj1.Conf;
        }
        while (0 < cands.Count) {
            // argmax(cscore[obj1])
            float mscore = -1;
            int mi = 0;
            for (int i = 0; i < cands.Count; i++) {
                float score = cscore[cands[i]];
                if (mscore < score) {
                    mscore = score; mi = i;
                }
            }
            if (mscore < request.Threshold) break;
            YLObject obj1 = cands[mi];
            objs.Add(obj1);
            cands.RemoveAt(mi);
            for (int i = 0; i < cands.Count; i++) {
                YLObject b1 = cands[i];
                float v = obj1.getIOU(b1);
                cscore[b1] *= Mathf.Exp(-3*v*v);
            }
        }

        DateTime t1 = DateTime.Now;
        YLResult result1 = new YLResult() {
            RequestId = request.RequestId,
            SentTime = t0,
            RecvTime = t1,
            InferenceTime = (float)((t1 - t0).TotalSeconds),
            Objects = objs.ToArray(),
        };
        //logit("recvData: result1={0}", result1);
        addResult(result1);
    }

    private float Sigmoid(float x) {
        return 1f/(1f+Mathf.Exp(-x));
    }

}

} // namespace net.sss_consortium.fastdet
