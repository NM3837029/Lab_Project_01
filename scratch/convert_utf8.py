import os

def convert_to_utf8_bom(filepath):
    try:
        # Read file with Shift-JIS or UTF-8
        content = None
        for encoding in ['utf-8-sig', 'shift_jis', 'utf-8', 'cp932']:
            try:
                with open(filepath, 'r', encoding=encoding) as f:
                    content = f.read()
                print(f"Successfully read {filepath} with {encoding}")
                break
            except Exception:
                continue
        
        if content is None:
            print("Failed to read file with any encoding.")
            return False
        
        # Write back as UTF-8 with BOM (utf-8-sig)
        with open(filepath, 'w', encoding='utf-8-sig') as f:
            f.write(content)
        print(f"Successfully converted {filepath} to UTF-8 with BOM.")
        return True
    except Exception as e:
        print(f"Error during conversion: {e}")
        return False

if __name__ == '__main__':
    convert_to_utf8_bom('DrawPixel.cpp')
