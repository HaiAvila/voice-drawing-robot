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
2. El orden en "subjects" importa: el primer objeto ocupa la posición del layout.
3. "layout" solo puede tener estos valores exactos:
   - "center"                   → un objeto centrado
   - "left_right"               → obj1 izquierda, obj2 derecha
   - "right_left"               → obj1 derecha, obj2 izquierda
   - "top_bottom"               → obj1 arriba, obj2 abajo
   - "bottom_top"               → obj1 abajo, obj2 arriba
   - "top_left_bottom_right"    → obj1 esquina sup-izq, obj2 esquina inf-der
   - "top_right_bottom_left"    → obj1 esquina sup-der, obj2 esquina inf-izq
   - "bottom_left_top_right"    → obj1 esquina inf-izq, obj2 esquina sup-der
   - "bottom_right_top_left"    → obj1 esquina inf-der, obj2 esquina sup-izq
4. Máximo 2 subjects.
5. Si hay relación espacial entre dos objetos, SIEMPRE usa el layout correspondiente. Nunca uses "center" cuando hay dos objetos con relación espacial.

RELACIONES ESPACIALES:
"sobre", "encima de", "arriba de", "por encima de"       → top_bottom
"debajo de", "abajo de", "bajo", "por debajo de"         → bottom_top
"a la izquierda de", "del lado izquierdo de"             → left_right
"a la derecha de", "del lado derecho de"                 → right_left
"junto a", "al lado de", "a lado de", "con"              → left_right (default horizontal)
"en diagonal", "esquina"                                  → top_left_bottom_right (default diagonal)
"arriba a la derecha"                                     → top_right_bottom_left
"arriba a la izquierda"                                   → top_left_bottom_right
"abajo a la derecha"                                      → bottom_right_top_left
"abajo a la izquierda"                                    → bottom_left_top_right

CUÁNDO USAR "unknown":
- Saludos o despedidas: "hola", "adiós", "qué tal"
- Preguntas sobre el sistema: "cómo funcinas", "qué eres", "para qué sirves"
- Comandos de control: "para", "detente", "cancela"
- Cualquier mensaje sin referencia a crear o dibujar algo visual.

CUÁNDO INTENTAR DIBUJAR (aunque sea vago):
- El usuario pide algo visual aunque no diga "dibuja": "quiero ver un perro", "ponme algo", "muéstrame un gato"
- El usuario dice qué puede dibujar el sistema: "dibuja algo bonito", "dibuja lo que quieras" → elige un objeto simple
- Preguntas sobre qué puede dibujar: "qué puedes dibujar" → elige y dibuja algo, no respondas unknown

EJEMPLOS:
"dibuja un perro"                                     → {"action": "draw", "subjects": ["dog"], "layout": "center"}
"una manzana sobre una mesa"                          → {"action": "draw", "subjects": ["apple", "table"], "layout": "top_bottom"}
"pon una mariposa sobre una flor"                     → {"action": "draw", "subjects": ["butterfly", "flower"], "layout": "top_bottom"}
"dibuja un libro encima de una silla"                 → {"action": "draw", "subjects": ["book", "chair"], "layout": "top_bottom"}
"dibuja una mochila abajo de una mesa"                → {"action": "draw", "subjects": ["backpack", "table"], "layout": "bottom_top"}
"un gato a la izquierda de un perro"                  → {"action": "draw", "subjects": ["cat", "dog"], "layout": "left_right"}
"un pez a la derecha de una roca"                     → {"action": "draw", "subjects": ["fish", "rock"], "layout": "right_left"}
"un sol en diagonal con una luna"                     → {"action": "draw", "subjects": ["sun", "moon"], "layout": "top_left_bottom_right"}
"dibuja algo bonito"                                  → {"action": "draw", "subjects": ["flower"], "layout": "center"}
"dibuja lo que quieras"                               → {"action": "draw", "subjects": ["star"], "layout": "center"}
"qué puedes dibujar"                                  → {"action": "draw", "subjects": ["cat"], "layout": "center"}
"algo bonito"                                         → {"action": "draw", "subjects": ["flower"], "layout": "center"}
"un animal"                                           → {"action": "draw", "subjects": ["dog"], "layout": "center"}
"hola cómo estás"                                     → {"action": "unknown"}
"para ya"                                             → {"action": "unknown"}
"cómo funcinas"                                       → {"action": "unknown"}"""

CASOS = [
    # Un objeto directo
    ("single_1",            "dibuja un perro"),
    ("single_2",            "hazme una mariposa"),
    ("single_3",            "trázame un elefante"),

    # Horizontal
    ("horiz_1",             "dibuja un gato junto a un perro"),
    ("horiz_2",             "quiero un árbol al lado de una casa"),
    ("horiz_3",             "un pájaro a la izquierda de un árbol"),
    ("horiz_4",             "pon un pez a la derecha de una roca"),

    # Vertical arriba
    ("vert_arriba_1",       "una manzana sobre una mesa"),
    ("vert_arriba_2",       "dibuja un libro encima de una silla"),
    ("vert_arriba_3",       "pon una mariposa sobre una flor"),
    ("vert_arriba_4",       "pon un sol en la parte de arriba y una casa abajo"),

    # Vertical abajo
    ("vert_abajo_1",        "dibuja una mochila abajo de una mesa"),
    ("vert_abajo_2",        "quiero un perro debajo de un árbol"),
    ("vert_abajo_3",        "una pelota bajo una silla"),

    # Diagonales
    ("diag_1",              "un sol en diagonal con una luna"),
    ("diag_2",              "dibuja un pájaro arriba a la derecha y un pez abajo a la izquierda"),
    ("diag_3",              "un gato en la esquina superior izquierda y un perro en la inferior derecha"),
    ("diag_4",              "una estrella arriba a la izquierda de una flor"),
    ("diag_5",              "dibuja un árbol abajo a la derecha de un pájaro"),

    # Frases naturales con relación espacial implícita
    ("natural_1",           "dibuja una manzana encima de una mesa como si estuviera de adorno"),
    ("natural_2",           "quiero un perrito junto a su casita"),
    ("natural_3",           "pon una mariposa sobre una flor"),
    ("natural_4",           "un pájaro posado sobre una rama"),
    ("natural_5",           "dibuja un gato durmiendo bajo un árbol"),

    # Ambiguos que SÍ deben intentar dibujar
    ("vago_1",              "algo bonito"),
    ("vago_2",              "dibuja lo que quieras"),
    ("vago_3",              "un animal"),
    ("vago_4",              "qué puedes dibujar"),
    ("vago_5",              "dibuja algo bonito"),
    ("vago_6",              "sorpréndeme"),

    # No son dibujo — deben ser unknown
    ("no_dibujo_1",         "hola cómo estás"),
    ("no_dibujo_2",         "para ya"),
    ("no_dibujo_3",         "cómo funcinas"),
    ("no_dibujo_4",         "gracias"),
    ("no_dibujo_5",         "cuánto mide el pizarrón"),
]

LAYOUTS_VALIDOS = {
    "center", "left_right", "right_left",
    "top_bottom", "bottom_top",
    "top_left_bottom_right", "top_right_bottom_left",
    "bottom_left_top_right", "bottom_right_top_left"
}

# Casos que específicamente deben devolver unknown
MUST_BE_UNKNOWN = {
    "no_dibujo_1", "no_dibujo_2", "no_dibujo_3", "no_dibujo_4", "no_dibujo_5"
}

# Casos que específicamente deben devolver draw
MUST_BE_DRAW = {
    "vago_1", "vago_2", "vago_3", "vago_4", "vago_5", "vago_6"
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

def evaluar(resultado, nombre):
    if resultado is None:
        return "FALLO_JSON"
    accion = resultado.get("action")
    if "action" not in resultado:
        return "FALLO_SIN_ACTION"

    # Casos que DEBEN ser unknown
    if nombre in MUST_BE_UNKNOWN and accion != "unknown":
        return f"FALLO_DEBE_SER_UNKNOWN (fue '{accion}')"

    # Casos que DEBEN ser draw
    if nombre in MUST_BE_DRAW and accion != "draw":
        return f"FALLO_DEBE_SER_DRAW (fue '{accion}')"

    if accion == "draw":
        subs = resultado.get("subjects", [])
        if not isinstance(subs, list) or len(subs) == 0:
            return "FALLO_SUBJECTS_INVALIDO"
        layout = resultado.get("layout", "")
        if layout not in LAYOUTS_VALIDOS:
            return f"FALLO_LAYOUT_INVALIDO: '{layout}'"
        for s in subs:
            if s.startswith(("a ", "an ", "the ")):
                return f"FALLO_SUBJECT_CON_ARTICULO: '{s}'"
        return "OK"

    if accion == "unknown":
        return "OK_UNKNOWN"

    return f"FALLO_ACTION_INVALIDO: '{accion}'"

if __name__ == "__main__":
    print(f"\n{'='*65}")
    print(f"  Prueba final: {MODELO}  |  Layouts + ambiguos")
    print(f"{'='*65}\n")

    ok_count  = 0
    fallos    = []
    latencias = []

    for nombre, texto in CASOS:
        resultado, latencia, error = llamar_llm(texto)
        estado = evaluar(resultado, nombre)
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
    print(f"  Resultado:     {ok_count}/{total} correctos ({ok_count/total*100:.0f}%)")
    print(f"  Latencia prom: {lat_prom:.2f}s")
    print(f"  Latencia máx:  {max(latencias):.2f}s")
    print(f"{'='*65}")

    if fallos:
        print("\n  Casos que fallaron:")
        for nombre, estado, texto in fallos:
            print(f"  - [{nombre}] '{texto}'")
            print(f"    → {estado}")
    else:
        print("\n  Todos los casos pasaron. Bloque 2 aprobado.")
    print()