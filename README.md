# FastDet

Fast object detector with distributed neural network.

## Prerequisites

### Client

 - Unity
 - Barracuda (> 2.0.0)

### Server

 - Python
 - Pillow
 - ONNX Runtime (w/ GPU)

    $ pip install -r requirements.txt

## Building Client (Unity)

 1. Launch Unity Hub and open a project.
 2. Select the "fastdet-test" folder.
 3. "File" → "Open Scene" and select the "SampleScene.unity".
 4. Open "Project" → "Assets" tab and make sure the "Yolov3-tiny" model is visible.
 5. Select "SampleScene" → "Canvas" and make sure the Yolo Model is associated with yolov3-tiny.
    (if missing, click it and connect to the yolov3-tiny.onnx)
 6. Connect the PC to a camera, press the Play button at the top.
 7. "File" → "Build Settings" and select "Android". Press "Switch Platform".
 8. Enable the "Developer Mode" and "USB Debugging" on an Android phone.
 9. Press "Build & Run".


## Testing

### Test detector only

    $ python server/detector.py -c 80 models/yolov3-full.onnx testdata/dog.jpg
    $ python server/detector.py -c 9 models/yolov3-rsu.onnx testdata/rsu1.jpg

### Test server with dummy detector

    $ python server/server.py -s 10000
    $ python server/client.py rtsp://localhost:10000/detect testdata/dog.jpg

### Test server with full detector

    $ python server/server.py -s 10000 full:80:models/yolov3-full.onnx rsu:9:models/yolov3-rsu.onnx
    $ python server/client.py rtsp://localhost:10000/full testdata/dog.jpg
    $ python server/client.py rtsp://localhost:10000/rsu testdata/rsu1.jpg

### Test server w/ CUDA

    $ python server/server.py -s 10000 -m cuda full:80:models/yolov3-full.onnx

### Debugging on Android

    > cd \Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools
    > adb logcat -c
    > adb logcat -s Unity


## Running

 1. launch the server.
 2. open the SampleScene.unity.
 3. configure the Server Url with the appropriate host/port.
 4. play the scene.


## TODOs

 - IPv6 support (both client and server).
 - Dockerize the server.
 - Rewrite the server in a faster language (Go or C# maybe?).
