#!/usr/bin/env python
##
##  client.py - test client
##
##  usage:
##    $ python client.py testdata/dog.jpg
##
import sys
import logging
import time
import selectors
import socket
import struct


##  RTSPClient
##
class RTSPClient:

    BUFSIZ = 65536

    def __init__(self, host, port, path='detect'):
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
        req = f'FEED {lport} {self.path}'
        self.logger.debug(f'send: req={req!r}')
        self.sock_rtsp.send(req.encode('ascii')+b'\r\n')
        resp = self.sock_rtsp.recv(self.BUFSIZ)
        self.logger.debug(f'recv: resp={resp!r}')
        if not resp.startswith(b'+OK '): raise IOError(resp)
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
        self.selector = selectors.DefaultSelector()
        self.fd = self.selector.register(self.sock_rtp, selectors.EVENT_READ)
        self._recv_buf = b''
        self._recv_seqno = 0
        self._send_seqno = 1
        return

    def request(self, reqid, threshold, data):
        header = struct.pack('>4sLLL', b'JPEG', reqid, int(threshold*100), len(data))
        self.send(header+data)
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
        for (fd, ev) in self.selector.select(timeout):
            if ev & selectors.EVENT_READ and fd == self.fd:
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
        if len(data) < 16: return # invalid data
        (tp, reqid, msec, length) = struct.unpack('>4sLLL', data[:16])
        data = data[16:]
        if len(data) != length: return # missing data
        i = 0
        result = []
        while i < len(data):
            (klass, conf, x, y, w, h) = struct.unpack(
                '>BBhhhh', data[i:i+10])
            result.append((klass, conf, x, y, w, h))
            i += 10
        self.logger.info(f'client: msec={msec}, reqid={reqid}, result={result}')
        return

# main
def main(argv):
    import getopt
    def usage():
        print(f'usage: {argv[0]} [-d] [-t interval] [-c host[:port]] [file ...]')
        return 100
    try:
        (opts, args) = getopt.getopt(argv[1:], 'dt:p:')
    except getopt.GetoptError:
        return usage()
    level = logging.INFO
    interval = 0.1
    client_host = 'localhost'
    client_port = 10000
    threshold = 0.3
    for (k, v) in opts:
        if k == '-d': level = logging.DEBUG
        elif k == '-t': interval = float(v)
        elif k == '-c':
            (host,_,port) = v.partition(':')
            if host:
                client_host = host
            if port:
                client_port = int(port)
    logging.basicConfig(format='%(asctime)s %(levelname)s %(message)s', level=level)

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
            client.request(reqid, threshold, data)
            client.idle()
            time.sleep(interval)
    return 0

if __name__ == '__main__': sys.exit(main(sys.argv))
