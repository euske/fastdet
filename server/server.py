#!/usr/bin/env python
##
##  server.py - server and test client.
##  - RFC 7826 (RTSP)
##  - RFC 3550 (RTP)
##  - RFC 2435 (RTP JPEG)
##
##  server$ python server.py -s 10000
##  client$ python server.py localhost 10000
##
import io
import sys
import logging
import socket
import select
import struct
import socketserver
import urllib.parse
import random
import base64
from datetime import datetime, timedelta


##  RTSPClient
##
class RTSPClient:

    BUFSIZ = 65536

    def __init__(self, host, port, endpoint='xxx'):
        self.logger = logging.getLogger()
        self.host = host
        self.port = port
        self.endpoint = endpoint
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
        self.logger.info(f'open: lport={lport}, host={self.host}, port={self.port}, endpoint={self.endpoint}')
        self.sock_rtsp = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock_rtsp.connect((self.host, self.port))
        self.logger.info(f'open: connected.')
        req = f'DETECT {lport} {self.endpoint}'
        self.logger.debug(f'send: {req}')
        self.sock_rtsp.send(req.encode('ascii')+b'\r\n')
        resp = self.sock_rtsp.recv(self.BUFSIZ)
        self.logger.debug(f'recv: {resp}')
        if resp.startswith(b'+OK '):
            f = resp[4:].strip().split()
            try:
                self.rtp_port = int(f[0])
                self.session_id = bytes.fromhex(f[1].decode('ascii'))
            except (UnicodeError, ValueError):
                raise
        assert self.rtp_port is not None
        # Send the dummy packet to initiate the stream.
        data = b'\x80\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
        self.sock_rtp.sendto(data, (self.host, self.rtp_port))
        self.epoll = select.epoll()
        self.epoll.register(self.sock_rtp, select.POLLIN)
        return

    def send(self, data):
        self.sock_rtp.sendto(data, (self.host, self.rtp_port))
        return

    def recv(self, timeout=0):
        # Poll RTP ports.
        for (fd,event) in epoll.poll(timeout):
            if fd == self.sock_rtp.fileno():
                (data, addr) = self.sock_rtp.recvfrom(self.BUFSIZ)
                self.process_rtp(data)
        return

    def process_rtp(self, data):
        print('rtp: {data!r}')
        return


##  RTSPServer
##
class RTSPHandler(socketserver.StreamRequestHandler):

    def setup(self):
        super().setup()
        self.logger = logging.getLogger()
        self.sock_rtp = None
        self.session_id = None
        self.rtp_host = None
        self.rtp_port = None
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
            else:
                self.wfile.write(b'!UNKNOWN\r\n')
                self.logger.error(f'handle: unknown command: req={req!r}')
                break
        return

    # handle_detect:
    def handle_detect(self, args):
        # DETECT endpoint clientport
        self.logger.info(f'handle_detect: args={args!r}')
        if self.sock_rtp is not None:
            self.wfile.write(b'!STATE\r\n')
            self.logger.error(f'handle_detect: already detect.')
            return
        flds = args.split()
        if len(flds) < 2:
            self.wfile.write(b'!INVALID\r\n')
            self.logger.error(f'handle_detect: invalid args: args={args!r}')
            return
        try:
            self.rtp_port = int(flds[0])
            endpoint = flds[1].decode('utf-8')
        except (UnicodeError, ValueError):
            self.wfile.write(b'!INVALID\r\n')
            self.logger.error(f'handle_detect: invalid args: args={args!r}')
            return
        (self.rtp_host, _) = self.client_address
        self.session_id = random.randbytes(4).hex()
        self.sock_rtp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock_rtp.setblocking(False)
        self.sock_rtp.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock_rtp.bind(('', 0))
        (_, port) = self.sock_rtp.getsockname()
        self.logger.info(f'handle_detect: port={port}, rtp_host={self.rtp_host}, rtp_port={self.rtp_port}, session_id={self.session_id}')
        text = f'+OK {port} {self.session_id}'
        self.wfile.write(text.encode('ascii')+b'\r\n')
        data = b'\x80\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
        self.sock_rtp.sendto(data, (self.rtp_host, self.rtp_port))
        return


# main
def main(argv):
    import getopt
    def usage():
        print(f'usage: {argv[0]} [-d] [-s port] [host [port]]')
        return 100
    try:
        (opts, args) = getopt.getopt(argv[1:], 'ds:')
    except getopt.GetoptError:
        return usage()
    level = logging.INFO
    host = '127.0.0.1'
    port = 10000
    server_port = None
    for (k, v) in opts:
        if k == '-d': level = logging.DEBUG
        elif k == '-s': server_port = int(v)

    logging.basicConfig(format='%(asctime)s %(levelname)s %(message)s', level=level)
    if args:
        host = args.pop(0)
    if args:
        port = int(args.pop(0))

    if server_port is not None:
        # Server mode.
        logging.info(f'listening: at {host}:{server_port}...')
        socketserver.TCPServer.allow_reuse_address = True
        with socketserver.TCPServer((host, server_port), RTSPHandler) as server:
            server.serve_forever()

    else:
        # Client mode.
        logging.info(f'connecting: {host}:{port}...')
        client = RTSPClient(host, port)
        client.open()
        while True:
            client.send(random.randbytes(100))
            client.recv()
    return 0

if __name__ == '__main__': sys.exit(main(sys.argv))
