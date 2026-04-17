grammar Ccgnf;

// -----------------------------------------------------------------------------
// CCGNF grammar — v1
//
// Parses POST-PREPROCESSOR input: the preprocessor consumes `define` blocks
// and expands macro invocations, so this grammar does not mention `define`.
//
// Scope: sufficient to parse tests/Ccgnf.Tests/fixtures/e2e-grammar-coverage.ccgnf
// after preprocessing. Some expressive edge cases noted in spec §5 are
// deferred (see grammar/GrammarSpec.md §12 "Open questions"):
//   - ASCII `in` as set-membership operator (Unicode ∈ is required here).
//   - Extended entity-indexing beyond single-ident bracket form.
// -----------------------------------------------------------------------------

// ---------- Parser rules ----------

file
    : declaration* EOF
    ;

declaration
    : entityDecl
    | cardDecl
    | tokenDecl
    | entityAugment
    ;

entityDecl
    : ENTITY name (LBRACK name RBRACK)? forClause? block
    ;

cardDecl
    : CARD name block
    ;

tokenDecl
    : TOKEN name block
    ;

// e.g., CoverageGame.abilities += Triggered(...)
entityAugment
    : qualifiedName PLUS_EQ expr
    ;

forClause
    : FOR name ELEMENT_OF expr
    ;

block
    : LBRACE field* RBRACE
    ;

// Field value may be:
//   * a nested block:               { sub: 1, sub2: 2 }
//   * a typed-derived-char form:    bool = self.x > 0
//   * an ordinary expression:       42
// Trailing comma is optional: fields may be separated by commas (inside
// braced literals like `{a: 1, b: 2}`) or just by whitespace/newlines
// (inside entity bodies).
field
    : fieldKey COLON fieldValue COMMA?
    ;

// Key may optionally be indexed (e.g., collapsed_for[Player1]).
fieldKey
    : name (LBRACK expr RBRACK)?
    ;

fieldValue
    : block
    | typedExpr
    | expr
    ;

// IDENT EQ expr form for derived characteristics (e.g., "bool = self.x > 0").
typedExpr
    : name EQ expr
    ;

qualifiedName
    : name (DOT name)+
    ;

// ---------- Expressions ----------

expr
    : orExpr
    ;

orExpr   : andExpr   ( OR  andExpr )* ;
andExpr  : notExpr   ( AND notExpr )* ;
notExpr  : NOT notExpr | relExpr ;
relExpr  : addExpr ( (EQ_EQ | NEQ | LE | GE | LT | GT | ELEMENT_OF | SUBSETEQ) addExpr )? ;
addExpr  : mulExpr ( (PLUS | MINUS) mulExpr )* ;
mulExpr  : unaryExpr ( (STAR | SLASH | CARTESIAN) unaryExpr )* ;
unaryExpr: (MINUS | PLUS) unaryExpr | postfixExpr ;

postfixExpr
    : atom trailer*
    ;

trailer
    : DOT name                           # TrailerMember
    | LBRACK expr RBRACK                 # TrailerIndex
    | LPAREN argList? RPAREN             # TrailerCall
    ;

atom
    : literal                            # AtomLiteral
    | lambda                             # AtomLambda
    | braceExpr                          # AtomBrace
    | listOrRange                        # AtomList
    | ifExpr                             # AtomIf
    | switchExpr                         # AtomSwitch
    | condExpr                           # AtomCond
    | whenExpr                           # AtomWhen
    | letExpr                            # AtomLet
    | name                               # AtomIdent
    | LPAREN expr (COMMA expr)* RPAREN   # AtomParen
    ;

literal
    : INT_LIT
    | STRING_LIT
    ;

// `{ ... }` may be either a set literal or a field block. We accept a mixed
// list of entries and let semantic analysis disambiguate.
braceExpr
    : LBRACE (braceEntry (COMMA braceEntry)* COMMA?)? RBRACE
    ;

braceEntry
    : field
    | expr
    ;

// `[ ... ]` is either a plain list `[a, b, c]` or a range `[1..5]`.
listOrRange
    : LBRACK expr DOTDOT expr RBRACK                    # ListRange
    | LBRACK (expr (COMMA expr)* COMMA?)? RBRACK        # ListLiteral
    ;

// Lambdas: `x -> body` and `(x, y) -> body`.
lambda
    : name ARROW expr                                   # LambdaSingle
    | LPAREN name (COMMA name)* RPAREN ARROW expr       # LambdaMulti
    ;

ifExpr
    : IF_KW LPAREN expr COMMA expr COMMA expr RPAREN
    ;

switchExpr
    : SWITCH_KW LPAREN expr COMMA LBRACE switchCase (COMMA switchCase)* COMMA? RBRACE RPAREN
    ;

switchCase
    : name COLON expr
    ;

condExpr
    : COND_KW LPAREN LBRACK condArm (COMMA condArm)* COMMA? RBRACK RPAREN
    ;

condArm
    : LPAREN expr COMMA expr RPAREN
    ;

whenExpr
    : WHEN_KW LPAREN expr COMMA expr (COMMA whenOpt)* RPAREN
    ;

whenOpt
    : name COLON expr
    ;

letExpr
    : LET name EQ expr IN_KW expr
    ;

argList
    : arg (COMMA arg)* COMMA?
    ;

arg
    : name COLON expr       # ArgNamed
    | name EQ expr          # ArgBinding     // Event.Kind(target=self) form
    | expr                  # ArgPositional
    ;

// Any identifier-like name. Accepts IDENT plus declaration/expression
// keywords used in identifier positions (e.g., `rarity: Token`,
// `Default` as a Switch case key). Semantic analysis disambiguates.
name
    : IDENT
    | ENTITY
    | CARD
    | TOKEN
    | FOR
    | IF_KW | SWITCH_KW | COND_KW | WHEN_KW
    | LET | IN_KW
    ;

// ---------- Lexer rules ----------

// Declaration keywords
ENTITY    : 'Entity' ;
CARD      : 'Card' ;
TOKEN     : 'Token' ;
FOR       : 'for' ;

// Expression keywords
IF_KW     : 'If' ;
SWITCH_KW : 'Switch' ;
COND_KW   : 'Cond' ;
WHEN_KW   : 'When' ;
LET       : 'let' ;
IN_KW     : 'in' ;

// Logical operators — ASCII and Unicode forms
AND        : 'and' | '\u2227' ;   // ∧
OR         : 'or'  | '\u2228' ;   // ∨
NOT        : 'not' | '\u00AC' ;   // ¬
ELEMENT_OF : '\u2208' ;           // ∈  (ASCII 'in' reserved for let-in only)
CARTESIAN  : '\u00D7' ;           // ×
SUBSETEQ   : '\u2286' ;           // ⊆

// Punctuation
ARROW   : '->' ;
PLUS_EQ : '+=' ;
DOTDOT  : '..' ;
EQ_EQ   : '==' ;
NEQ     : '!=' ;
LE      : '<=' ;
GE      : '>=' ;
LT      : '<' ;
GT      : '>' ;
PLUS    : '+' ;
MINUS   : '-' ;
STAR    : '*' ;
SLASH   : '/' ;
EQ      : '=' ;
LBRACE  : '{' ;
RBRACE  : '}' ;
LBRACK  : '[' ;
RBRACK  : ']' ;
LPAREN  : '(' ;
RPAREN  : ')' ;
COMMA   : ',' ;
COLON   : ':' ;
DOT     : '.' ;

// Literals
IDENT      : [a-zA-Z_] [a-zA-Z_0-9]* ;
INT_LIT    : [0-9]+ ;
STRING_LIT : '"' ( ~["\\\r\n] | '\\' . )* '"' ;

// Trivia
LINE_COMMENT  : '//' ~[\r\n]* -> skip ;
BLOCK_COMMENT : '/*' .*? '*/' -> skip ;
WS            : [ \t\r\n]+ -> skip ;
