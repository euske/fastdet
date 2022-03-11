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
from detector import DummyDetector, ONNXDetector

##  RTPHandler
##
class RTPHandler:

    BUFSIZ = 65536

    def __init__(self, server, sock_rtp, rtp_host, rtp_port, session_id, timeout=10):
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
        self._active = 0
        return

    def __repr__(self):
        return f'<RTPHandler: rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}>'

    def open(self):
        self.logger.info(f'open: rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}>')
        data = b'\x80\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
        self.sock_rtp.sendto(data, (self.rtp_host, self.rtp_port))
        self._send_seqno += 1
        self._active = time.time()
        return

    def close(self):
        assert self.sock_rtp is not None
        self.logger.info(f'close')
        self.sock_rtp.close()
        self.sock_rtp = None
        return False

    def is_alive(self, t):
        return (t < self._active+self.timeout)

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
        self._active = time.time()
        return

    def process_data(self, data):
        self.logger.debug(f'server: process_data: {len(data)}')
        if len(data) < 12: return # invalid data
        (tp, reqid, length) = struct.unpack('>4sLL', data[:12])
        data = data[12:]
        if len(data) != length: return # missing data
        if self.server.dbgout is not None:
            with open(self.server.dbgout, 'wb') as fp:
                fp.write(data)
        t0 = time.time()
        result = b''
        for (klass, conf, x, y, w, h) in self.server.detector.perform(data):
            result += struct.pack(
                '>BBhhhh', klass, int(conf*255),
                int(x), int(y), int(w), int(h))
        msec = int((time.time() - t0)*1000)
        header = struct.pack('>4sLLL', b'YOLO', reqid, msec, len(result))
        self.send(header+result)
        return

##  RTSPServer
##
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

    def __init__(self, server_address, detector, dbgout=None):
        super().__init__(server_address, RTSPHandler)
        self.detector = detector
        self.dbgout = dbgout
        self.logger = logging.getLogger()
        self.epoll = select.epoll()
        self.handlers = {}
        return

    def service_actions(self):
        super().service_actions()
        for (fd,event) in self.epoll.poll(0):
            if fd in self.handlers:
                handler = self.handlers[fd]
                handler.idle()
        t = time.time()
        for (fd,handler) in list(self.handlers.items()):
            if not handler.is_alive(t):
                self.unregister(fd)
        return

    def register(self, fd, handler):
        self.logger.info(f'register: fd={fd}, handler={handler}')
        self.epoll.register(fd, select.POLLIN)
        self.handlers[fd] = handler
        return

    def unregister(self, fd):
        assert fd in self.handlers
        self.logger.info(f'unregister: fd={fd}')
        self.epoll.unregister(fd)
        handler = self.handlers[fd]
        handler.close()
        del self.handlers[fd]
        return


# main
def main(argv):
    import getopt
    def usage():
        print(f'usage: {argv[0]} [-d] [-o dbgout] [-m mode] [-s port] [-t interval] [args]')
        return 100
    try:
        (opts, args) = getopt.getopt(argv[1:], 'do:m:s:t:')
    except getopt.GetoptError:
        return usage()
    level = logging.INFO
    mode = None
    server_port = 10000
    interval = 0.1
    dbgout = None
    for (k, v) in opts:
        if k == '-d': level = logging.DEBUG
        elif k == '-o': dbgout = v
        elif k == '-m': mode = v
        elif k == '-s': server_port = int(v)
        elif k == '-t': interval = float(v)
    logging.basicConfig(format='%(asctime)s %(levelname)s %(message)s', level=level)

    # Server mode.
    if args:
        detector = ONNXDetector(args[0], mode=mode)
    else:
        detector = DummyDetector()
    logging.info(f'listening: at {server_port}...')
    RTSPServer.allow_reuse_address = True
    timeout = 0.05
    with RTSPServer(('', server_port), detector, dbgout=dbgout) as server:
        server.serve_forever(timeout)
    return 0

if __name__ == '__main__': sys.exit(main(sys.argv))
