#!/usr/bin/env python
##
##  server.py - server and test client.
##  - RFC 7826 (RTSP)
##  - RFC 3550 (RTP)
##  - RFC 2435 (RTP JPEG)
##
##  server$ python server.py -s 10000
##  client$ python server.py -c :10000
##
import io
import sys
import logging
import time
import select
import socket
import socketserver
import struct
import random
from math import exp
from datetime import datetime, timedelta

def sigmoid(x):
    return 1/(1+exp(-x))

def argmax(a, key=lambda x:x):
    (imax, vmax) = (None, None)
    for (i,x) in enumerate(a):
        v = key(x)
        if vmax is None or vmax < v:
            (imax, vmax) = (i, v)
    if imax is None: raise ValueError(a)
    return (imax, vmax)

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
    score = { obj:obj.conf for obj in objs }
    while objs:
        (i,conf) = argmax(objs, key=lambda obj:score[obj])
        if conf < threshold: break
        m = objs[i]
        result.append(m)
        del objs[i]
        for obj in objs:
            v = m.get_iou(obj.bbox)
            score[obj] = score[obj] * exp(-3*v*v)
    result.sort(key=lambda obj:score[obj], reverse=True)
    return result


##  Detector
##
class DummyDetector:

    def perform(self, data):
        (klass, conf, x, y, w, h) = (1,255,10,10,100,100)
        return [(klass, conf, x, y, w, h)]

class ONNXDetector:

    IMAGE_SIZE = (416,416)
    NUM_CLASS = 80
    ANCHORS = ((81/32, 82/32), (135/32,169/32), (344/32,319/32))

    def __init__(self, path, mode=None):
        import onnxruntime as ort
        providers = ['CPUExecutionProvider']
        if mode == 'cuda':
            providers.insert(0, 'CUDAExecutionProvider')
        self.model = ort.InferenceSession(path, providers=providers)
        self.logger = logging.getLogger()
        self.logger.info(f'load: path={path}, providers={providers}')
        return

    def perform(self, data):
        from PIL import Image
        import numpy as np
        (width, height) = self.IMAGE_SIZE
        img = Image.open(io.BytesIO(data))
        if img.size != self.IMAGE_SIZE:
            raise ValueError('invalid image size')
        a = (np.array(img).reshape(1,height,width,3)/255).astype(np.float32)
        objs = []
        for output in self.model.run(None, {'input': a}):
            objs.extend(self.process_yolo(output[0]))
        objs = soft_nms(objs, threshold=0.3)
        results = [ (obj.klass, int(obj.conf*255),
                     int(obj.bbox[0]*width), int(obj.bbox[1]*height),
                     int(obj.bbox[2]*width), int(obj.bbox[3]*height)) for obj in objs ]
        self.logger.info(f'perform: results={results}')
        return results

    def process_yolo(self, m, threshold=0.1):
        (rows,cols,_) = m.shape
        a = []
        for (y,row) in enumerate(m):
            for (x,col) in enumerate(row):
                for (k,(ax,ay)) in enumerate(self.ANCHORS):
                    b = k*(5+self.NUM_CLASS)
                    x = sigmoid(col[b+0]) / cols
                    y = sigmoid(col[b+1]) / rows
                    w = ax * exp(col[b+2]) / cols
                    h = ay * exp(col[b+3]) / rows
                    conf = sigmoid(col[b+4])
                    mp = mi = None
                    for i in range(self.NUM_CLASS):
                        p = col[b+5+i]
                        if mp is None or mp < p:
                            (mp,mi) = (p,i)
                    conf *= sigmoid(mp)
                    if threshold < conf:
                        a.append(YOLOObject(mi, conf, (x, y, w, h)))
        return a

    @classmethod
    def test_detector(klass):
        detector = klass('./models/yolov3-tiny.onnx')
        with open('./testdata/dog.jpg', 'rb') as fp:
            data = fp.read()
        print(detector.perform(data))
        return


##  RTSPClient
##
class RTSPClient:

    BUFSIZ = 65536

    def __init__(self, host, port, path='xxx'):
        self.logger = logging.getLogger()
        self.host = host
        self.port = port
        self.path = path
        self.sock_rtp = None
        self.sock_rtsp = None
        self.session_id = None
        self.rtp_port = None
        return

    def open(self):
        self.sock_rtp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock_rtp.setblocking(False)
        self.sock_rtp.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock_rtp.bind(('', 0))
        (_, lport) = self.sock_rtp.getsockname()
        self.logger.info(f'open: lport={lport}, host={self.host}, port={self.port}, path={self.path}')
        self.sock_rtsp = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock_rtsp.connect((self.host, self.port))
        self.logger.info(f'open: connected.')
        req = f'DETECT {lport} {self.path}'
        self.logger.debug(f'send: req={req!r}')
        self.sock_rtsp.send(req.encode('ascii')+b'\r\n')
        resp = self.sock_rtsp.recv(self.BUFSIZ)
        self.logger.debug(f'recv: resp={resp!r}')
        if resp.startswith(b'+OK '):
            f = resp[4:].strip().split()
            try:
                self.rtp_port = int(f[0])
                self.session_id = bytes.fromhex(f[1].decode('ascii'))
            except (UnicodeError, ValueError):
                raise
        self.logger.info(f'open: rtp_port={self.rtp_port}, session_id={self.session_id.hex()}')
        assert self.rtp_port is not None
        # Send the dummy packet to initiate the stream.
        data = b'\x80\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
        self.sock_rtp.sendto(data, (self.host, self.rtp_port))
        self.epoll = select.epoll()
        self.epoll.register(self.sock_rtp.fileno(), select.POLLIN)
        self._recv_buf = b''
        self._recv_seqno = 0
        self._send_seqno = 1
        return

    def send(self, data, chunk_size=32768):
        i0 = 0
        while i0 < len(data):
            i1 = i0 + chunk_size
            pt = 96
            if len(data) <= i1:
                pt |= 0x80
            header = struct.pack('>BBH', 0x80, pt, self._send_seqno & 0xffff)
            self._send_seqno += 1
            segment = data[i0:i1]
            self.sock_rtp.sendto(header+segment, (self.host, self.rtp_port))
            i0 = i1
        return

    def idle(self, timeout=0):
        # Poll RTP ports.
        for (fd,event) in self.epoll.poll(timeout):
            if fd == self.sock_rtp.fileno():
                while True:
                    try:
                        (data, addr) = self.sock_rtp.recvfrom(self.BUFSIZ)
                        self.process_rtp(data)
                    except BlockingIOError:
                        break
        return

    def process_rtp(self, data):
        (flags,pt,seqno) = struct.unpack('>BBH', data[:4])
        self.logger.debug(
            f'process_rtp: flags={flags}, pt={pt}, seqno={seqno}')
        if self._recv_seqno != seqno:
            # Packet drop detected. Cancelling the current payload.
            self.logger.info(f'process_rtp: DROP {seqno}/{self._recv_seqno}')
            self._recv_buf = None
        if (pt & 0x7f) == 96 and self._recv_buf is not None:
            self._recv_buf += data[4:]
        if pt & 0x80:
            # Significant packet - ending the payload.
            if self._recv_buf is not None:
                self.process_data(self._recv_buf)
            self._recv_buf = b''
        self._recv_seqno = seqno+1
        return

    def process_data(self, data):
        self.logger.debug(f'client: process_data: len={len(data)}')
        if 12 < len(data):
            (tp, reqid, msec, length) = struct.unpack('>4sLLL', data[:16])
            data = data[16:]
            if len(data) == length:
                i = 0
                result = []
                while i < len(data):
                    (klass, conf, x, y, w, h) = struct.unpack('>BBhhhh', data[i:i+10])
                    result.append((klass, conf, x, y, w, h))
                    i += 10
                self.logger.info(f'client: msec={msec}, reqid={reqid}, result={result}')
        return


##  RTSPServer
##
class RTPHandler:

    BUFSIZ = 65536

    def __init__(self, server, sock_rtp, rtp_host, rtp_port, session_id, timeout=3):
        self.logger = logging.getLogger(session_id.hex())
        self.server = server
        self.sock_rtp = sock_rtp
        self.rtp_host = rtp_host
        self.rtp_port = rtp_port
        self.session_id = session_id
        self.timeout = timeout
        self._recv_buf = b''
        self._recv_seqno = 0
        self._send_seqno = 0
        return

    def __repr__(self):
        return f'<RTPHandler: rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}>'

    def open(self):
        self.logger.info(f'open: rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}>')
        data = b'\x80\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
        self.sock_rtp.sendto(data, (self.rtp_host, self.rtp_port))
        self._send_seqno += 1
        return

    def close(self):
        assert self.sock_rtp is not None
        self.logger.info(f'close')
        self.sock_rtp.close()
        self.sock_rtp = None
        return

    def send(self, data, chunk_size=32768):
        i0 = 0
        while i0 < len(data):
            i1 = i0 + chunk_size
            pt = 96
            if len(data) <= i1:
                pt |= 0x80
            header = struct.pack('>BBH', 0x80, pt, self._send_seqno & 0xffff)
            self._send_seqno += 1
            segment = data[i0:i1]
            self.sock_rtp.sendto(header+segment, (self.rtp_host, self.rtp_port))
            i0 = i1
        return

    def idle(self):
        assert self.sock_rtp is not None
        while True:
            try:
                (data, addr) = self.sock_rtp.recvfrom(self.BUFSIZ)
                self.process_rtp(data)
            except BlockingIOError:
                break
        return

    def process_rtp(self, data):
        (flags,pt,seqno) = struct.unpack('>BBH', data[:4])
        self.logger.debug(
            f'process_rtp: flags={flags}, pt={pt}, seqno={seqno}')
        if self._recv_seqno != seqno:
            # Packet drop detected. Cancelling the current payload.
            self.logger.info(f'process_rtp: DROP {seqno}/{self._recv_seqno}')
            self._recv_buf = None
        if (pt & 0x7f) == 96 and self._recv_buf is not None:
            self._recv_buf += data[4:]
        if pt & 0x80:
            # Significant packet - ending the payload.
            if self._recv_buf is not None:
                self.process_data(self._recv_buf)
            self._recv_buf = b''
        self._recv_seqno = seqno+1
        return

    def process_data(self, data):
        self.logger.debug(f'server: process_data: {len(data)}')
        if 12 < len(data):
            (tp, reqid, length) = struct.unpack('>4sLL', data[:12])
            data = data[12:]
            if len(data) == length:
                t0 = time.time()
                result = b''
                for (klass, conf, x, y, w, h) in self.server.detector.perform(data):
                    result += struct.pack('>BBhhhh', klass, conf, x, y, w, h)
                msec = int((time.time() - t0)*1000)
                header = struct.pack('>4sLLL', b'YOLO', reqid, msec, len(result))
                self.send(header+result)
        return

class RTSPHandler(socketserver.StreamRequestHandler):

    def setup(self):
        super().setup()
        self.logger = logging.getLogger()
        self.logger.info(f'setup')
        return

    def finish(self):
        super().finish()
        self.logger.info(f'finish')
        return

    def handle(self):
        self.logger.info(f'handle: client_address={self.client_address}')
        while True:
            req = self.rfile.readline()
            if not req: break
            self.logger.debug(f'handle: req={req!r}')
            (cmd,_,args) = req.strip().partition(b' ')
            cmd = cmd.upper()
            if cmd == b'DETECT':
                self.handle_detect(args)
                break
            else:
                self.wfile.write(b'!UNKNOWN\r\n')
                self.logger.error(f'handle: unknown command: req={req!r}')
                break
        return

    # handle_detect: "DETECT path clientport"
    def handle_detect(self, args):
        self.logger.debug(f'handle_detect: args={args!r}')
        flds = args.split()
        if len(flds) < 2:
            self.wfile.write(b'!INVALID\r\n')
            self.logger.error(f'handle_detect: invalid args: args={args!r}')
            return
        try:
            rtp_port = int(flds[0])
            path = flds[1].decode('utf-8')
        except (UnicodeError, ValueError):
            self.wfile.write(b'!INVALID\r\n')
            self.logger.error(f'handle_detect: invalid args: args={args!r}')
            return
        (rtp_host, _) = self.client_address
        # random.randbytes() is only supported in 3.9.
        session_id = bytes( random.randrange(256) for _ in range(4) )
        sock_rtp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock_rtp.setblocking(False)
        sock_rtp.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock_rtp.bind(('', 0))
        (_, port) = sock_rtp.getsockname()
        self.logger.info(f'handle_detect: port={port}, rtp_host={rtp_host}, rtp_port={rtp_port}, session_id={session_id.hex()}')
        text = f'+OK {port} {session_id.hex()}'
        self.wfile.write(text.encode('ascii')+b'\r\n')
        handler = RTPHandler(self.server, sock_rtp, rtp_host, rtp_port, session_id)
        self.server.register(sock_rtp.fileno(), handler)
        handler.open()
        return

class RTSPServer(socketserver.TCPServer):

    def __init__(self, server_address, detector):
        super().__init__(server_address, RTSPHandler)
        self.detector = detector
        self.logger = logging.getLogger()
        self.epoll = select.epoll()
        self.handlers = {}
        return

    def service_actions(self):
        super().service_actions()
        timestamp = time.time()
        for (fd,event) in self.epoll.poll(0):
            if fd in self.handlers:
                (_,handler) = self.handlers[fd]
                handler.idle()
                self.handlers[fd] = (timestamp, handler)
        for (fd,(t,handler)) in list(self.handlers.items()):
            if t + handler.timeout < timestamp:
                self.unregister(fd)
        return

    def register(self, fd, handler):
        self.logger.info(f'register: fd={fd}, handler={handler}')
        self.epoll.register(fd, select.POLLIN)
        self.handlers[fd] = (time.time(), handler)
        return

    def unregister(self, fd):
        assert fd in self.handlers
        self.logger.info(f'unregister: fd={fd}')
        self.epoll.unregister(fd)
        (_, handler) = self.handlers[fd]
        handler.close()
        del self.handlers[fd]
        return


# main
def main(argv):
    import getopt
    def usage():
        print(f'usage: {argv[0]} [-d] [-m mode] [-s port] [-c host:port]] [-i interval]')
        return 100
    try:
        (opts, args) = getopt.getopt(argv[1:], 'dm:s:c:i:')
    except getopt.GetoptError:
        return usage()
    level = logging.INFO
    mode = None
    server_port = 10000
    client_host = None
    client_port = server_port
    interval = 0.1
    for (k, v) in opts:
        if k == '-d': level = logging.DEBUG
        elif k == '-m': mode = v
        elif k == '-s': server_port = int(v)
        elif k == '-c':
            (client_host,_,x) = v.partition(':')
            if not client_host:
                client_host = 'localhost'
            if x:
                client_port = int(x)
        elif k == '-t': interval = float(v)
    logging.basicConfig(format='%(asctime)s %(levelname)s %(message)s', level=level)

    if client_host is not None:
        # Client mode.
        logging.info(f'connecting: {client_host}:{client_port}...')
        client = RTSPClient(client_host, client_port)
        client.open()
        files = []
        for path in args:
            with open(path, 'rb') as fp:
                files.append(fp.read())
        reqid = 0
        while True:
            for data in files:
                reqid += 1
                header = struct.pack('>4sLL', b'JPEG', reqid, len(data))
                client.send(header+data)
                client.idle()
                time.sleep(interval)
    else:
        # Server mode.
        logging.info(f'listening: at {server_port}...')
        RTSPServer.allow_reuse_address = True
        timeout = 0.05
        if args:
            detector = ONNXDetector(args[0], mode=mode)
        else:
            detector = DummyDetector()
        with RTSPServer(('', server_port), detector) as server:
            server.serve_forever(timeout)

    return 0

if __name__ == '__main__': sys.exit(main(sys.argv))
