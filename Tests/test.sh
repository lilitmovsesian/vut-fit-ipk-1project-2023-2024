#!/bin/bash

chmod +x test.sh

# Test 1
test_output=$(../ipk24chat-client -s anton5.fit.vutbr.cz -t incorrectProtocol 2>&1)
expected_output="ERR: Invalid transport protocol. Use 'udp' or 'tcp'."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 1: Passed"
else
    echo "Test 1: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 2
test_output=$(../ipk24chat-client -s anton5.fit.vutbr.cz 2>&1)
expected_output="ERR: Invalid program parameters, transport protocol and server IP address or hostname can't be null."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 2: Passed"
else
    echo "Test 2: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 3
test_output=$(../ipk24chat-client -t udp 2>&1)
expected_output="ERR: Invalid program parameters, transport protocol and server IP address or hostname can't be null."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 3: Passed"
else
    echo "Test 3: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 4
test_output=$(../ipk24chat-client -s anton5.fit.vutbr.cz -t tcp -d 65536 2>&1)
expected_output="ERR: Invalid value for -d. Please provide a valid UInt16 value."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 4: Passed"
else
    echo "Test 4: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 5
test_output=$(../ipk24chat-client -s anton5.fit.vutbr.cz -t tcp -p 65536 2>&1)
expected_output="ERR: Invalid value for -p. Please provide a valid UInt16 value."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 5: Passed"
else
    echo "Test 5: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 6
test_output=$(../ipk24chat-client -s anton5.fit.vutbr.cz -t tcp -r 256 2>&1)
expected_output="ERR: Invalid value for -r. Please provide a valid UInt8 value."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 6: Passed"
else
    echo "Test 6: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 7
test_output=$(../ipk24chat-client -s anton5.fit.vutbr.cz -h 2>&1)
expected_output="ERR: Invalid program parameters."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 7: Passed"
else
    echo "Test 7: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 8
test_output=$(../ipk24chat-client 2>&1)
expected_output="ERR: Invalid program parameters, transport protocol and server IP address or hostname can't be null."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 8: Passed"
else
    echo "Test 8: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 9
test_output=$(cat ./TestsStdin/TestStdin6.txt | ../ipk24chat-client -s anton5.fit.vutbr.cz -t udp 2>&1)
expected_output="ERR: Error sending a message in non-open state."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 9: Passed"
else
    echo "Test 9: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 10
test_output=$(cat ./TestsStdin/TestStdin5.txt | ../ipk24chat-client -s anton5.fit.vutbr.cz -t tcp 2>&1)
expected_output="ERR: /auth command is required. Use /auth {Username} {Secret} {DisplayName}."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 10: Passed"
else
    echo "Test 10: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 11
test_output=$(cat ./TestsStdin/TestStdin5.txt | ../ipk24chat-client -s anton5.fit.vutbr.cz -t udp 2>&1)
expected_output="ERR: /auth command is required. Use /auth {Username} {Secret} {DisplayName}."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 11: Passed"
else
    echo "Test 11: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 12
test_output=$(cat ./TestsStdin/TestStdin6.txt | ../ipk24chat-client -s anton5.fit.vutbr.cz -t tcp 2>&1)
expected_output="ERR: Error sending a message in non-open state."
if [ "$test_output" = "$expected_output" ]; then
    echo "Test 12: Passed"
else
    echo "Test 12: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 13
test_output=$(cat ./TestsStdin/TestStdin7.txt | ../ipk24chat-client -s anton5.fit.vutbr.cz -t udp 2>&1)
expected_output="Failure:*."
if [[ "$test_output" == $expected_output ]]; then
    echo "Test 13: Passed"
else
    echo "Test 13: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi

# Test 14
test_output=$(cat ./TestsStdin/TestStdin7.txt | ../ipk24chat-client -s anton5.fit.vutbr.cz -t tcp 2>&1)
expected_output="Failure:*."
if [[ "$test_output" == $expected_output ]]; then
    echo "Test 14: Passed"
else
    echo "Test 14: Failed"
    echo "Expected: $expected_output"
    echo "Actual: $test_output"
fi
