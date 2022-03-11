#!/usr/bin/env python
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
import struct
import random
from math import exp
from datetime import datetime, timedelta

class SocketHandler:

    def __init__(self, sock):
        self.logger = logging.getLogger()
        self.sock = sock
        self.loop = None
        return

    def idle(self):
        return True

    def doit(self, ev):
        return

    def close(self):
        self.sock.close()
        self.sock = None
        self.loop = None
        self.logger.info(f'closed: {self}')
        return

class EventLoop:

    def __init__(self):
        self.logger = logging.getLogger()
        self.poll = select.epoll()
        self.handlers = {}
        return

    def add(self, handler):
        fd = handler.sock.fileno()
        assert fd not in self.handlers
        self.poll.register(fd)
        self.handlers[fd] = handler
        self.logger.info(f'added: {handler}')
        handler.loop = self
        return

    def run(self, interval=0.1):
        while True:
            for (fd, ev) in self.poll.poll(interval):
                if fd in self.handlers:
                    handler = self.handlers[fd]
                    handler.doit(ev)
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

class Service(SocketHandler):

    BUFSIZ = 65535

    def __init__(self, server, sock, addr):
        super().__init__(sock)
        self.server = server
        self.addr = addr
        self.alive = True
        self.buf = b''
        return

    def __repr__(self):
        return f'<Service: addr={self.addr}>'

    def idle(self):
        return self.alive

    def doit(self, ev):
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

class Server(SocketHandler):

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
        return f'<Server: port={self.port}>'

    def doit(self, ev):
        (conn, addr) = self.sock.accept()
        self.logger.info(f'accept: {addr}')
        self.loop.add(Service(self, conn, addr))
        return

class RTSPServer(Server):
    pass

# main
def main(argv):
    import getopt
    def usage():
        print(f'usage: {argv[0]} [-d] [-o dbgout] [-m mode] [-s port] [-c host:port]] [-i interval]')
        return 100
    try:
        (opts, args) = getopt.getopt(argv[1:], 'do:m:s:c:i:')
    except getopt.GetoptError:
        return usage()
    level = logging.INFO
    mode = None
    server_port = 10000
    client_host = None
    client_port = server_port
    interval = 0.1
    dbgout = None
    for (k, v) in opts:
        if k == '-d': level = logging.DEBUG
        elif k == '-o': dbgout = v
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
        loop = EventLoop()
        loop.add(RTSPServer(server_port))
        loop.run()

    return 0

if __name__ == '__main__': sys.exit(main(sys.argv))
