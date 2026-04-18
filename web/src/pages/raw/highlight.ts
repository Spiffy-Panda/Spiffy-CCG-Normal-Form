// Minimal regex-based CCGNF highlighter. Tokenises a single scan of the
// source and emits spans with hl-* classes; not a real parser. Enough to
// skim files in the Raw view.

const KEYWORDS =
  /\b(Entity|Card|Token|Augment|define|for|in|let|If|Switch|Cond|When|Default|true|false|None|Unbound|NoOp)\b/;

const OPERATORS = /[∈∧∨∩∪⊆×¬]|->|\+=|==|!=|<=|>=/;

interface Rule {
  name: string;
  re: RegExp;
}

const RULES: Rule[] = [
  { name: "comment-block", re: /\/\*[\s\S]*?\*\// },
  { name: "comment-line", re: /\/\/[^\n]*/ },
  { name: "string", re: /"(?:[^"\\]|\\.)*"/ },
  { name: "keyword", re: KEYWORDS },
  { name: "operator", re: OPERATORS },
  { name: "number", re: /\b\d+\b/ },
];

const combined = new RegExp(
  RULES.map((r, i) => `(?<g${i}>${r.re.source})`).join("|"),
  "g",
);

export function highlightCcgnf(source: string): DocumentFragment {
  const frag = document.createDocumentFragment();
  let cursor = 0;
  combined.lastIndex = 0;
  let match: RegExpExecArray | null;
  while ((match = combined.exec(source)) !== null) {
    if (match.index > cursor) {
      frag.appendChild(document.createTextNode(source.slice(cursor, match.index)));
    }
    let ruleName = "";
    for (let i = 0; i < RULES.length; i++) {
      if (match.groups?.[`g${i}`] !== undefined) {
        ruleName = RULES[i].name;
        break;
      }
    }
    const span = document.createElement("span");
    span.className = classFor(ruleName);
    span.textContent = match[0];
    frag.appendChild(span);
    cursor = match.index + match[0].length;
  }
  if (cursor < source.length) {
    frag.appendChild(document.createTextNode(source.slice(cursor)));
  }
  return frag;
}

function classFor(ruleName: string): string {
  switch (ruleName) {
    case "comment-line":
    case "comment-block":
      return "hl-comment";
    case "string":
      return "hl-string";
    case "keyword":
      return "hl-keyword";
    case "operator":
      return "hl-operator";
    case "number":
      return "hl-number";
    default:
      return "";
  }
}
