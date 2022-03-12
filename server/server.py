#!/usr/bin/env python
##
##  server$ python server.py -s 10000
##
import sys
import logging
import time
import select
import socket
import struct
import random
from detector import DummyDetector, ONNXDetector


##  SocketHandler
##
class SocketHandler:

    def __init__(self, sock):
        self.logger = logging.getLogger()
        self.sock = sock
        self.loop = None
        return

    def idle(self):
        return True

    def action(self, ev):
        return

    def close(self):
        self.sock.close()
        self.sock = None
        self.loop = None
        self.logger.info(f'closed: {self}')
        return


##  TCPService
##
class TCPService(SocketHandler):

    BUFSIZ = 65535

    def __init__(self, server, sock):
        super().__init__(sock)
        self.server = server
        self.addr = self.sock.getsockname()
        self.alive = True
        self.buf = b''
        return

    def __repr__(self):
        return f'<{self.__class__.__name__}: addr={self.addr}>'

    def idle(self):
        return self.alive

    def action(self, ev):
        data = self.sock.recv(self.BUFSIZ)
        if data:
            i0 = 0
            while i0 < len(data):
                i1 = data.find(b'\n', i0)
                if i1 < 0:
                    self.buf += data[i0:]
                    break
                self.buf += data[i0:i1+1]
                self.feedline(self.buf)
                self.buf = b''
                i0 = i1+1
        else:
            if self.buf:
                self.feedline(self.buf)
            self.alive = False
        return

    def feedline(self, line):
        return


##  UDPService
##
class UDPService(SocketHandler):

    BUFSIZ = 65535

    def __init__(self, server, sock, timeout=10):
        super().__init__(sock)
        self.server = server
        self.addr = self.sock.getsockname()
        self.alive = True
        return

    def __repr__(self):
        return f'<{self.__class__.__name__}: addr={self.addr}>'

    def idle(self):
        return self.alive

    def shutdown(self):
        self.alive = False
        return

    def action(self, ev):
        (data, addr) = self.sock.recvfrom(self.BUFSIZ)
        self.recvdata(data, addr)
        return

    def recvdata(self, data, addr):
        return


##  TCPServer
##
class TCPServer(SocketHandler):

    def __init__(self, port):
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind(('', port))
        sock.listen(1)
        super().__init__(sock)
        self.port = port
        self.logger.info(f'listening: port={port}...')
        return

    def __repr__(self):
        return f'<{self.__class__.__name__}: port={self.port}>'

    def action(self, ev):
        (conn, addr) = self.sock.accept()
        self.logger.info(f'accept: {addr}')
        self.loop.add(self.get_service(conn))
        return

    def get_service(self, conn):
        return TCPService(self, conn)


##  EventLoop
##
class EventLoop:

    def __init__(self):
        self.logger = logging.getLogger()
        self.poll = select.epoll()
        self.handlers = {}
        return

    def add(self, handler):
        fd = handler.sock.fileno()
        assert fd not in self.handlers
        self.poll.register(fd, select.EPOLLIN)
        self.handlers[fd] = handler
        self.logger.info(f'added: {handler}')
        handler.loop = self
        return

    def run(self, interval=0.1):
        while True:
            for (fd, ev) in self.poll.poll(interval):
                if ev & select.EPOLLIN and fd in self.handlers:
                    handler = self.handlers[fd]
                    handler.action(ev)
            self.idle()
        return

    def idle(self):
        removed = []
        for (fd, handler) in self.handlers.items():
            if not handler.idle():
                removed.append((fd, handler))
        for (fd, handler) in removed:
            self.poll.unregister(fd)
            del self.handlers[fd]
            self.logger.info(f'removed: {handler}')
            handler.close()
        return


##  RTPService
##
class RTPService(UDPService):

    CHUNK_SIZE = 40000

    def __init__(self, server, sock, rtp_host, rtp_port, session_id, timeout=10):
        super().__init__(server, sock)
        self.rtp_host = rtp_host
        self.rtp_port = rtp_port
        self.session_id = session_id
        self.timeout = timeout
        self._recv_buf = b''
        self._recv_seqno = 0
        self._send_seqno = 0
        return

    def __repr__(self):
        return f'<{self.__class__.__name__}: rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}>'

    def init(self):
        self.logger.info(f'init: rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}>')
        data = b'\x80\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
        self.sock.sendto(data, (self.rtp_host, self.rtp_port))
        self._send_seqno += 1
        self._active = time.time()
        return

    def recvdata(self, data, addr):
        if addr != (self.rtp_host, self.rtp_port): return
        (flags,pt,seqno) = struct.unpack('>BBH', data[:4])
        self.logger.debug(
            f'recv: flags={flags}, pt={pt}, seqno={seqno}')
        if self._recv_seqno != seqno:
            # Packet drop detected. Cancelling the current payload.
            self.logger.info(f'recv: DROP {seqno}/{self._recv_seqno}')
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
        self.logger.debug(f'process_data: {len(data)}')
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

    def send(self, data, chunk_size=CHUNK_SIZE):
        i0 = 0
        while i0 < len(data):
            i1 = i0 + chunk_size
            pt = 96
            if len(data) <= i1:
                pt |= 0x80
            header = struct.pack('>BBH', 0x80, pt, self._send_seqno & 0xffff)
            self._send_seqno += 1
            segment = data[i0:i1]
            self.sock.sendto(header+segment, (self.rtp_host, self.rtp_port))
            i0 = i1
        return

##  RTSPService
##
class RTSPService(TCPService):

    def feedline(self, req):
        (cmd,_,args) = req.strip().partition(b' ')
        cmd = cmd.upper()
        if cmd == b'FEED':
            self.startfeed(args)
        else:
            self.sock.send(b'!UNKNOWN\r\n')
            self.logger.error(f'unknown command: req={req!r}')
        return

    def close(self):
        super().close()
        self.rtpservice.shutdown()
        return

    # startfeed: "FEED clientport path"
    def startfeed(self, args):
        self.logger.debug(f'startfeed: args={args!r}')
        flds = args.split()
        if len(flds) < 2:
            self.sock.send(b'!INVALID\r\n')
            self.logger.error(f'startfeed: invalid args: args={args!r}')
            return
        try:
            rtp_port = int(flds[0])
            path = flds[1].decode('utf-8')
        except (UnicodeError, ValueError):
            self.sock.send(b'!INVALID\r\n')
            self.logger.error(f'startfeed: invalid args: args={args!r}')
            return
        (rtp_host, _) = self.sock.getpeername()
        # random.randbytes() is only supported in 3.9.
        session_id = bytes( random.randrange(256) for _ in range(4) )
        sock_rtp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock_rtp.setblocking(False)
        sock_rtp.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock_rtp.bind(('', 0))
        (_, port) = sock_rtp.getsockname()
        self.logger.info(f'startfeed: port={port}, rtp_host={rtp_host}, rtp_port={rtp_port}, session_id={session_id.hex()}')
        text = f'+OK {port} {session_id.hex()}'
        self.sock.send(text.encode('ascii')+b'\r\n')
        self.rtpservice = RTPService(
            self.server, sock_rtp, rtp_host, rtp_port, session_id)
        self.loop.add(self.rtpservice)
        self.rtpservice.init()
        return

##  RTSPServer
##
class RTSPServer(TCPServer):

    def __init__(self, port, detector, dbgout=None):
        super().__init__(port)
        self.detector = detector
        self.dbgout = dbgout
        return

    def get_service(self, conn):
        return RTSPService(self, conn)

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
    loop = EventLoop()
    loop.add(RTSPServer(server_port, detector, dbgout=dbgout))
    loop.run(interval)
    return 0

if __name__ == '__main__': sys.exit(main(sys.argv))
