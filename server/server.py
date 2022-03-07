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
from datetime import datetime, timedelta


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
        self.logger.debug(f'send: {req!r}')
        self.sock_rtsp.send(req.encode('ascii')+b'\r\n')
        resp = self.sock_rtsp.recv(self.BUFSIZ)
        self.logger.debug(f'recv: {resp!r}')
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
        return

    def send(self, data):
        self.sock_rtp.sendto(data, (self.host, self.rtp_port))
        return

    def recv(self, timeout=0):
        # Poll RTP ports.
        for (fd,event) in self.epoll.poll(timeout):
            if fd == self.sock_rtp.fileno():
                (data, addr) = self.sock_rtp.recvfrom(self.BUFSIZ)
                self.process_rtp(data)
        return

    def process_rtp(self, data):
        print(f'rtp: {data!r}')
        return


##  RTSPServer
##
class RTPHandler:

    BUFSIZ = 65536

    def __init__(self, sock_rtp, rtp_host, rtp_port, session_id):
        self.logger = logging.getLogger(session_id.hex())
        self.sock_rtp = sock_rtp
        self.rtp_host = rtp_host
        self.rtp_port = rtp_port
        self.session_id = session_id
        data = b'\x80\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
        self.sock_rtp.sendto(data, (self.rtp_host, self.rtp_port))
        self.logger.info(f'init: rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}>')
        self.timeout = 3
        return

    def __repr__(self):
        return f'<RTPHandler: rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}>'

    def close(self):
        assert self.sock_rtp is not None
        self.logger.info(f'close')
        self.sock_rtp.close()
        self.sock_rtp = None
        return

    def recv(self):
        assert self.sock_rtp is not None
        data = self.sock_rtp.recv(self.BUFSIZ)
        self.logger.debug(f'recv: {data!r}')
        self.sock_rtp.sendto(data, (self.rtp_host, self.rtp_port))
        return True

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
        session_id = random.randbytes(4)
        sock_rtp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock_rtp.setblocking(False)
        sock_rtp.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock_rtp.bind(('', 0))
        (_, port) = sock_rtp.getsockname()
        self.logger.info(f'handle_detect: port={port}, rtp_host={rtp_host}, rtp_port={rtp_port}, session_id={session_id.hex()}')
        text = f'+OK {port} {session_id.hex()}'
        self.wfile.write(text.encode('ascii')+b'\r\n')
        handler = RTPHandler(sock_rtp, rtp_host, rtp_port, session_id)
        self.server.register(sock_rtp.fileno(), handler)
        return

class RTSPServer(socketserver.TCPServer):

    def __init__(self, server_address):
        super().__init__(server_address, RTSPHandler)
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
                handler.recv()
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
        self.handlers[fd].close()
        del self.handlers[fd]
        return


# main
def main(argv):
    import getopt
    def usage():
        print(f'usage: {argv[0]} [-d] [-s port] [-c host:port]]')
        return 100
    try:
        (opts, args) = getopt.getopt(argv[1:], 'ds:c:')
    except getopt.GetoptError:
        return usage()
    level = logging.INFO
    server_port = 10000
    client_host = None
    client_port = server_port
    for (k, v) in opts:
        if k == '-d': level = logging.DEBUG
        elif k == '-s': server_port = int(v)
        elif k == '-c':
            (client_host,_,x) = v.partition(':')
            if not client_host:
                client_host = 'localhost'
            if x:
                client_port = int(x)
    logging.basicConfig(format='%(asctime)s %(levelname)s %(message)s', level=level)

    if client_host is not None:
        # Client mode.
        logging.info(f'connecting: {client_host}:{client_port}...')
        client = RTSPClient(client_host, client_port)
        client.open()
        while True:
            client.send(random.randbytes(100))
            client.recv()
            time.sleep(0.1)
    else:
        # Server mode.
        logging.info(f'listening: at {server_port}...')
        RTSPServer.allow_reuse_address = True
        timout = 0.05
        with RTSPServer(('', server_port)) as server:
            server.serve_forever(timeout)

    return 0

if __name__ == '__main__': sys.exit(main(sys.argv))