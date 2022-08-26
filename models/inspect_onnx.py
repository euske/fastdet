#!/usr/bin/env python
import sys
import logging
import onnx
import onnx.numpy_helper

def read_const(graph, name):
    for node in graph.node:
        if node.name == name:
            print(name)
            return
        if name in node.output:
            for (i,oname) in enumerate(node.output):
                if oname == name:
                    value = None
                    for v in node.attribute:
                        if v.name == 'value':
                            value = onnx.numpy_helper.to_array(v.t)
                    print(f"{node.name}:{i}({oname}) {value}")
                    return
    for t in graph.initializer:
        if t.name == name:
            tensor = onnx.numpy_helper.to_array(t)
            print(f"{name}={tensor} \n{tensor.dtype} {tensor.shape}")
    return

def main(argv):
    import getopt
    def usage():
        print('usage: %s onnx_model [node_name | output_name | initialzer_name] ...' % argv[0])
        return 100
    try:
        (opts, args) = getopt.getopt(argv[1:], 'do:')
    except getopt.GetoptError:
        return usage()
    level = logging.INFO
    output = None
    for (k, v) in opts:
        if k == '-d': level = logging.DEBUG
        elif k == '-o': output = v
    if not args: return usage()

    logging.basicConfig(format='%(asctime)s %(levelname)s %(message)s', level=level)

    # show printable graph
    model = onnx.load(args.pop(0))
    if not args:
        print(onnx.helper.printable_graph(model.graph))
        return

    # inspect values
    for name in args:
        if name.startswith('%'):
            name = name[1:]
        read_const(model.graph, name)

    return

if __name__ == '__main__': sys.exit(main(sys.argv))
