#!/bin/bash

chmod +x test.sh

cat ../TestStdin7.txt | ../../../../ipk24chat-client -s anton5.fit.vutbr.cz -t udp &> ./TestOutput.txt
