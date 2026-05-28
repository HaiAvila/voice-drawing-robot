import requests
import json
import time
import re

MODELO = "phi3.5"
URL_OLLAMA = "http://localhost:11434/api/chat"

PROMPT_SISTEMA = """Eres un intérprete de comandos de dibujo robótico.
Responde ÚNICAMENTE con JSON. Sin texto adicional. Sin explicaciones.

FORMATO:
- Un objeto:   {"action": "draw", "subjects": ["palabra"], "layout": "center"}
- Dos objetos: {"action": "draw", "subjects": ["palabra1", "palabra2"], "layout": "<layout>"}
- No es dibujo: {"action": "unknown"}

REGLAS:
1. "subjects" contiene sustantivos simples en inglés minúsculas. Sin artículos ni adjetivos.
   Correcto: "dog", "apple", "table"  |  Incorrecto: "a dog", "big apple"
2. El orden en "subjects" importa: el primer objeto ocupa la posición indicada por el layout.
3. "layout" tiene estos valores posibles SOLAMENTE:
   - "center"                   → un objeto centrado
   - "left_right"               → obj1 a la izquierda, obj2 a la derecha
   - "right_left"               → obj1 a la derecha, obj2 a la izquierda
   - "top_bottom"               → obj1 arriba, obj2 abajo
   - "bottom_top"               → obj1 abajo, obj2 arriba
   - "top_left_bottom_right"    → obj1 esquina superior izquierda, obj2 esquina inferior derecha
   - "top_right_bottom_left"    → obj1 esquina superior derecha, obj2 esquina inferior izquierda
   - "bottom_left_top_right"    → obj1 esquina inferior izquierda, obj2 esquina superior derecha
   - "bottom_right_top_left"    → obj1 esquina inferior derecha, obj2 esquina superior izquierda
4. Si el mensaje no pide dibujar algo concreto: {"action": "unknown"}
5. Máximo 2 subjects.

RELACIONES ESPACIALES → LAYOUT:
"sobre", "encima de", "arriba de", "en la parte de arriba"  → top_bottom (obj1 arriba, obj2 abajo)
"debajo de", "abajo de", "bajo", "en la parte de abajo"     → bottom_top (obj1 abajo, obj2 arriba)
"a la izquierda de", "del lado izquierdo"                   → left_right (obj1 izq, obj2 der)
"a la derecha de", "del lado derecho"                       → right_left (obj1 der, obj2 izq)
"junto a", "al lado de", "a lado de"                        → left_right (default horizontal)
"en diagonal", "en la esquina"                              → top_left_bottom_right (default diagonal)
"arriba a la derecha", "esquina superior derecha"           → top_right_bottom_left
"arriba a la izquierda", "esquina superior izquierda"       → top_left_bottom_right

EJEMPLOS:
"dibuja un perro"                                 → {"action": "draw", "subjects": ["dog"], "layout": "center"}
"una manzana sobre una mesa"                      → {"action": "draw", "subjects": ["apple", "table"], "layout": "top_bottom"}
"dibuja un libro encima de una silla"             → {"action": "draw", "subjects": ["book", "chair"], "layout": "top_bottom"}
"dibuja una mochila abajo de una mesa"            → {"action": "draw", "subjects": ["backpack", "table"], "layout": "bottom_top"}
"un gato a la izquierda de un perro"              → {"action": "draw", "subjects": ["cat", "dog"], "layout": "left_right"}
"quiero un pájaro a la derecha de un árbol"       → {"action": "draw", "subjects": ["bird", "tree"], "layout": "right_left"}
"una flor junto a una casa"                       → {"action": "draw", "subjects": ["flower", "house"], "layout": "left_right"}
"un sol en diagonal con una luna"                 → {"action": "draw", "subjects": ["sun", "moon"], "layout": "top_left_bottom_right"}
"un pez arriba a la derecha de una roca"          → {"action": "draw", "subjects": ["fish", "rock"], "layout": "top_right_bottom_left"}
"hola cómo estás"                                 → {"action": "unknown"}
"para ya"                                         → {"action": "unknown"}
"qué puedes hacer"                                → {"action": "unknown"}"""

CASOS = [
    # Un objeto
    ("single_1",        "dibuja un perro"),
    ("single_2",        "hazme una mariposa"),
    ("single_3",        "trázame un elefante"),

    # Horizontal
    ("horiz_1",         "dibuja un gato junto a un perro"),
    ("horiz_2",         "quiero un árbol al lado de una casa"),
    ("horiz_3",         "un pájaro a la izquierda de un árbol"),
    ("horiz_4",         "pon un pez a la derecha de una roca"),

    # Vertical — obj1 arriba
    ("vert_arriba_1",   "una manzana sobre una mesa"),
    ("vert_arriba_2",   "dibuja un libro encima de una silla"),
    ("vert_arriba_3",   "quiero un pájaro arriba de un árbol"),
    ("vert_arriba_4",   "pon un sol en la parte de arriba y una casa abajo"),

    # Vertical — obj1 abajo
    ("vert_abajo_1",    "dibuja una mochila abajo de una mesa"),
    ("vert_abajo_2",    "quiero un perro debajo de un árbol"),
    ("vert_abajo_3",    "una pelota bajo una silla"),

    # Diagonales
    ("diag_1",          "un sol en diagonal con una luna"),
    ("diag_2",          "dibuja un pájaro arriba a la derecha y un pez abajo a la izquierda"),
    ("diag_3",          "un gato en la esquina superior izquierda y un perro en la inferior derecha"),
    ("diag_4",          "una estrella arriba a la izquierda de una flor"),

    # Frases naturales / implícitas
    ("natural_1",       "dibuja una manzana encima de una mesa como si estuviera de adorno"),
    ("natural_2",       "quiero un perrito junto a su casita"),
    ("natural_3",       "pon una mariposa sobre una flor"),

    # No son dibujo
    ("no_dibujo_1",     "hola cómo estás"),
    ("no_dibujo_2",     "para ya"),
    ("no_dibujo_3",     "qué puedes dibujar"),
]

LAYOUTS_VALIDOS = {
    "center", "left_right", "right_left",
    "top_bottom", "bottom_top",
    "top_left_bottom_right", "top_right_bottom_left",
    "bottom_left_top_right", "bottom_right_top_left"
}

def extraer_json(texto):
    try:
        return json.loads(texto)
    except json.JSONDecodeError:
        match = re.search(r'\{.*?\}', texto, re.DOTALL)
        if match:
            try:
                return json.loads(match.group())
            except json.JSONDecodeError:
                return None
        return None

def llamar_llm(texto):
    inicio = time.time()
    try:
        response = requests.post(URL_OLLAMA, json={
            "model": MODELO,
            "stream": False,
            "format": "json",
            "messages": [
                {"role": "system", "content": PROMPT_SISTEMA},
                {"role": "user",   "content": texto}
            ]
        }, timeout=30)
        latencia = time.time() - inicio
        contenido = response.json()["message"]["content"].strip()
        resultado = extraer_json(contenido)
        if resultado is None:
            return None, latencia, f"JSON inválido: {contenido}"
        return resultado, latencia, None
    except Exception as e:
        return None, time.time() - inicio, f"Error: {e}"

def evaluar(resultado):
    if resultado is None:
        return "FALLO_JSON"
    if "action" not in resultado:
        return "FALLO_SIN_ACTION"
    if resultado["action"] == "draw":
        subs = resultado.get("subjects", [])
        if not isinstance(subs, list) or len(subs) == 0:
            return "FALLO_SUBJECTS_INVALIDO"
        layout = resultado.get("layout", "")
        if layout not in LAYOUTS_VALIDOS:
            return f"FALLO_LAYOUT_INVALIDO: '{layout}'"
        # Verificar que subjects no tienen artículos
        for s in subs:
            if s.startswith(("a ", "an ", "the ")):
                return f"FALLO_SUBJECT_CON_ARTICULO: '{s}'"
        return "OK"
    if resultado["action"] == "unknown":
        return "OK_UNKNOWN"
    return f"FALLO_ACTION_INVALIDO: '{resultado['action']}'"

if __name__ == "__main__":
    print(f"\n{'='*65}")
    print(f"  Prueba de LLM: {MODELO}  |  Layouts espaciales")
    print(f"{'='*65}\n")

    ok_count  = 0
    fallos    = []
    latencias = []

    for nombre, texto in CASOS:
        resultado, latencia, error = llamar_llm(texto)
        estado = evaluar(resultado)
        latencias.append(latencia)

        simbolo = "✓" if "OK" in estado else "✗"
        print(f"{simbolo} [{nombre}]")
        print(f"  Input:   '{texto}'")
        print(f"  Output:  {json.dumps(resultado, ensure_ascii=False) if resultado else error}")
        print(f"  Estado:  {estado}  |  Latencia: {latencia:.2f}s")
        print()

        if "OK" in estado:
            ok_count += 1
        else:
            fallos.append((nombre, estado, texto))

    total    = len(CASOS)
    lat_prom = sum(latencias) / len(latencias)

    print(f"{'='*65}")
    print(f"  Resultado:       {ok_count}/{total} correctos ({ok_count/total*100:.0f}%)")
    print(f"  Latencia prom:   {lat_prom:.2f}s")
    print(f"  Latencia máx:    {max(latencias):.2f}s")
    print(f"{'='*65}")

    if fallos:
        print("\n  Casos que fallaron:")
        for nombre, estado, texto in fallos:
            print(f"  - [{nombre}] '{texto}'")
            print(f"    {estado}")
    print()