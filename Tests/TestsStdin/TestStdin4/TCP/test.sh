#!/bin/bash

chmod +x test.sh

cat ../TestStdin4.txt | ../../../../ipk24chat-client -s anton5.fit.vutbr.cz -t tcp &> ./TestOutput.txt
