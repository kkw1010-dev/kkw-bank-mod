import os
import re

html_path = r'c:\TAKEALOOK\BankPrismUI\PrismaUI\views\BankPrism\BankView.html'
out_dir = r'c:\TAKEALOOK\BankPrismUI\Previews'
os.makedirs(out_dir, exist_ok=True)

with open(html_path, 'r', encoding='utf-8') as f:
    content = f.read()

holds = [
    ('Whiterun', '화이트런', 'hold-whiterun'),
    ('Haafingar', '솔리튜드', 'hold-haafingar'),
    ('Eastmarch', '윈드헬름', 'hold-eastmarch'),
    ('TheRift', '리프튼', 'hold-rift'),
    ('TheReach', '마르카스', 'hold-reach'),
    ('Winterhold', '윈터홀드', 'hold-winterhold'),
    ('ThePale', '던스타', 'hold-pale'),
    ('Hjaalmarch', '모탈', 'hold-hjaalmarch'),
    ('Falkreath', '포크리스', 'hold-falkreath')
]

for en_name, kr_name, css_class in holds:
    # Replace body tag
    new_html = re.sub(r'<body.*?>', f'<body class="{css_class}">', content, count=1)
    # Replace holdName text
    new_html = re.sub(r'<span id="holdName">.*?</span>', f'<span id="holdName">{kr_name}</span>', new_html)
    
    out_file = os.path.join(out_dir, f'Preview_{en_name}.html')
    with open(out_file, 'w', encoding='utf-8') as out_f:
        out_f.write(new_html)

print('Previews generated successfully.')
