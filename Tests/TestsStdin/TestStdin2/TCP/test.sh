#!/bin/bash

chmod +x test.sh

cat ../TestStdin2.txt | ../../../../ipk24chat-client -s anton5.fit.vutbr.cz -t tcp &> ./TestOutput.txt
