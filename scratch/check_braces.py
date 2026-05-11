import sys

content = open('DrawPixel.cpp', 'r', encoding='utf-8').read()
stack = []
for i, char in enumerate(content):
    if char == '{':
        stack.append(i)
    elif char == '}':
        if not stack:
            print(f"Extra closing brace at index {i}")
            # Find line number
            line_no = content[:i].count('\n') + 1
            print(f"Line {line_no}")
        else:
            stack.pop()

if stack:
    for s in stack:
        line_no = content[:s].count('\n') + 1
        print(f"Unclosed brace at index {s}, Line {line_no}")
