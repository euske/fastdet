#!/usr/bin/env python
##
##  detector.py - YOLO object detection
##
import io
import sys
import logging
import numpy as np
import time
from math import exp

def sigmoid(x):
    return 1/(1+exp(-x))

def rect_intersect(rect0, rect1):
    (x0,y0,w0,h0) = rect0
    (x1,y1,w1,h1) = rect1
    x = max(x0, x1)
    y = max(y0, y1)
    w = min(x0+w0, x1+w1) - x
    h = min(y0+h0, y1+h1) - y
    return (x, y, w, h)


##  YOLOObject
##
class YOLOObject:

    def __init__(self, klass, conf, bbox):
        self.klass = klass
        self.conf = conf
        self.bbox = bbox
        return

    def __repr__(self):
        return (f'<YOLOObject({self.klass}): conf={self.conf:.3f}, bbox={self.bbox}>')

    def get_iou(self, bbox):
        (_,_,w,h) = rect_intersect(self.bbox, bbox)
        if w <= 0 or h <= 0: return 0
        (_,_,w0,h0) = self.bbox
        return (w*h)/(w0*h0)

# soft_nms: https://arxiv.org/abs/1704.04503
def soft_nms(objs, threshold):
    result = []
    cands = { obj:obj.conf for obj in objs }
    while objs:
        (mconf,mobj) = (-1,None)
        for (obj,conf) in cands.items():
            if mconf < conf:
                (mconf,mobj) = (conf,obj)
        if mconf < threshold: break
        result.append((mconf, mobj))
        del cands[mobj]
        cands = { obj: conf*exp(-3*(mobj.get_iou(obj.bbox)**2))
                  for (obj,conf) in cands.items() }
    result.sort(reverse=True)
    return [ obj for (_,obj) in result ]


##  Detector
##
class Detector:

    IMAGE_SIZE = (416,416)
    NUM_CLASS = 80

    def __init__(self, dbgout=None):
        self.dbgout = dbgout
        return

    def perform(self, data):
        if self.dbgout is not None:
            with open(self.dbgout, 'wb') as fp:
                fp.write(data)
        return

class DummyDetector(Detector):

    def perform(self, data, threshold=0.3):
        super().perform(data)
        (width, height) = self.IMAGE_SIZE
        klass = 16              # cat
        conf = 1.0
        x = 0.5*width
        y = 0.5*height
        w = 0.4*width
        h = 0.4*height
        return [(klass, conf, x, y, w, h)]

class ONNXDetector(Detector):

    ANCHORS = {
        # yolov3-full (3 outputs)
        3: (((116, 90), (156, 198), (373, 326)),
            ((30, 61), (62, 45), (59, 119)),
            ((10, 13), (16, 30), (33, 23))
            ),
        # yolov3-tiny (2 outputs)
        2: (((81, 82), (135, 169), (344, 319)),
            ((10, 14), (23, 27), (37, 58)),
            ),
    }

    def __init__(self, path, mode=None, dbgout=None):
        super().__init__(dbgout=dbgout)
        import onnxruntime as ort
        providers = ['CPUExecutionProvider']
        if mode == 'cuda':
            providers.insert(0, 'CUDAExecutionProvider')
        elif mode == 'tensorrt':
            providers.insert(0, 'TensorrtExecutionProvider')
        self.model = ort.InferenceSession(path, providers=providers)
        self.logger = logging.getLogger()
        self.logger.info(f'load: path={path}, providers={providers}')
        return

    def perform(self, data, threshold=0.3):
        super().perform(data)
        from PIL import Image
        (width, height) = self.IMAGE_SIZE
        img = Image.open(io.BytesIO(data))
        if img.size != self.IMAGE_SIZE:
            raise ValueError('invalid image size')
        a = (np.array(img).reshape(1,height,width,3)/255).astype(np.float32)
        outputs = self.model.run(None, {'input': a})
        aas = self.ANCHORS[len(outputs)]
        objs = []
        for (anchors,output) in zip(aas, outputs):
            objs.extend(self.process_yolo(anchors, output[0], threshold=threshold))
        objs = soft_nms(objs, threshold=threshold)
        results = [ (obj.klass, obj.conf,
                     obj.bbox[0]*width, obj.bbox[1]*height,
                     obj.bbox[2]*width, obj.bbox[3]*height) for obj in objs ]
        self.logger.info(f'perform: results={results}')
        return results

    def process_yolo(self, anchors, m, threshold=0.3):
        (width, height) = self.IMAGE_SIZE
        (rows,cols,_) = m.shape
        a = []
        for (y0,row) in enumerate(m):
            for (x0,col) in enumerate(row):
                for (k,(ax,ay)) in enumerate(anchors):
                    b = (5+self.NUM_CLASS) * k
                    conf = sigmoid(col[b+4])
                    if conf < threshold: continue
                    x = (x0 + sigmoid(col[b+0])) / cols
                    y = (y0 + sigmoid(col[b+1])) / rows
                    w = ax * exp(col[b+2]) / width
                    h = ay * exp(col[b+3]) / height
                    mi = np.argmax(col[b+5:b+5+self.NUM_CLASS])
                    conf *= sigmoid(col[b+5+mi])
                    if conf < threshold: continue
                    a.append(YOLOObject(mi+1, conf, (x-w/2, y-h/2, w, h)))
        return a

# main
def main(argv):
    args = argv[1:]
    path = args.pop(0)
    detector = ONNXDetector(path)
    for path in args:
        with open(path, 'rb') as fp:
            data = fp.read()
        t0 = time.time()
        result = detector.perform(data)
        dt = time.time() - t0
        print(dt, result)
    return
if __name__ == '__main__': sys.exit(main(sys.argv))
