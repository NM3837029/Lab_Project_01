def check_braces_level(filepath):
    try:
        with open(filepath, 'r', encoding='shift-jis') as f:
            lines = f.readlines()
    except UnicodeDecodeError:
        with open(filepath, 'r', encoding='utf8', errors='ignore') as f:
            lines = f.readlines()
            
    level = 0
    stack = []
    for i, line in enumerate(lines):
        line_num = i + 1
        stripped = line.strip()
        
        # Skip comments
        if stripped.startswith('//') or stripped.startswith('/*') or stripped.startswith('*'):
            continue
            
        for char_num, char in enumerate(line):
            if char == '{':
                level += 1
                stack.append((line_num, line.strip()))
            elif char == '}':
                level -= 1
                if stack:
                    open_line, open_text = stack.pop()
                    if any(k in open_text for k in ["WinMain", "while", "for", "else if", "if"]):
                        print(f"L{line_num}: Closed '{open_text}' (from L{open_line}) -> Nest level: {level}")
                else:
                    print(f"L{line_num}: Extra '}}' found! -> Nest level: {level}")

check_braces_level(r"C:\Users\naots\Documents\OriginalGame\C++\Lab_Project_01\DrawPixel.cpp")
