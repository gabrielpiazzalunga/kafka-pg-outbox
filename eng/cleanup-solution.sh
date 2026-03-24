#!/bin/bash
find ./ -type d \( -name 'bin' -o -name 'obj' \) -exec rm -rf {} +

echo "All 'bin' and 'obj' directories have been removed."