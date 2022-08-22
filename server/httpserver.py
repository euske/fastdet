#!/usr/bin/env python
import sys
import logging
from http.server import HTTPServer, BaseHTTPRequestHandler

class MyHTTPHandler(BaseHTTPRequestHandler):

    def __init__(self, *args, **kwargs):
        self.logger = logging.getLogger()
        super().__init__(*args, **kwargs)

    def do_HEAD(self):
        self.send_response(200)

    def do_GET(self):
        self.logger.info(f'{self.command}: path={self.path}')
        if self.path != '/':
            self.send_response(404)
            self.send_header('Content-Type', 'text/plain')
            self.end_headers()
            self.wfile.write(b'not found')
            return
        self.send_response(200)
        self.send_header('Content-Type', 'text/plain')
        self.end_headers()
        data = (self.requestline, dict(self.headers))
        self.wfile.write(repr(data).encode('utf-8'))

def main(argv):
    import getopt
    def usage():
        print(f'usage: {argv[0]} [-d] [-s port]')
        return 100
    try:
        (opts, args) = getopt.getopt(argv[1:], 'ds:')
    except getopt.GetoptError:
        return usage()
    level = logging.INFO
    server_port = 10000
    for (k, v) in opts:
        if k == '-d': level = logging.DEBUG
        elif k == '-s': server_port = int(v)
    logging.basicConfig(format='%(asctime)s %(levelname)s %(message)s', level=level)

    logging.info(f'listening: port={server_port}...')
    with HTTPServer(('', server_port), MyHTTPHandler) as httpd:
        httpd.serve_forever()
    return 0

if __name__ == '__main__': sys.exit(main(sys.argv))
