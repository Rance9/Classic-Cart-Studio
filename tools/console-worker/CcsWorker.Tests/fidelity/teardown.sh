#!/bin/bash
# Test only: removes the disposable folder setup.sh made (and nothing else).
case "$1" in /tmp/ccsfid-*) rm -rf -- "$1";; *) echo "not a fidelity folder" >&2; exit 125;; esac
