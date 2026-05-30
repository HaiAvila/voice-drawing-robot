import requests
import json
import time
import re

MODELO = "phi3.5"
URL_OLLAMA = "http://localhost:11434/api/chat"

PROMPT_SISTEMA = """Eres un intérprete de comandos de dibujo robótico.
Responde ÚNICAMENTE con JSON. Sin texto adicional. Sin explicaciones fuera del JSON.

FORMATO OBLIGATORIO — siempre incluye todos los campos:
- Un objeto:   {"action": "draw", "subjects": ["palabra"], "layout": "center", "message": "..."}
- Dos objetos: {"action": "draw", "subjects": ["palabra1", "palabra2"], "layout": "...", "message": "..."}
- No es dibujo: {"action": "unknown", "message": "..."}

LAYOUTS — copia el nombre EXACTAMENTE, sin abreviar, sin modificar:
  "center"                   un objeto centrado
  "left_right"               obj1 izquierda, obj2 derecha
  "right_left"               obj1 derecha, obj2 izquierda
  "top_bottom"               obj1 arriba, obj2 abajo
  "bottom_top"               obj1 abajo, obj2 arriba
  "top_left_bottom_right"    obj1 esquina sup-izq, obj2 esquina inf-der
  "top_right_bottom_left"    obj1 esquina sup-der, obj2 esquina inf-izq
  "bottom_left_top_right"    obj1 esquina inf-izq, obj2 esquina sup-der
  "bottom_right_top_left"    obj1 esquina inf-der, obj2 esquina sup-izq

REGLAS DE SUBJECTS:
1. Sustantivos simples en inglés minúsculas. Sin artículos ni adjetivos.
2. Máximo 2 subjects.
3. SIEMPRE deben ser objetos dibujables y concretos: animales, plantas, objetos del hogar, naturaleza.
   Si el usuario dice "lo que quieras", "sorpréndeme" o algo vago → elige tú: "flower", "cat", "star".
   NUNCA uses: "you", "surprise", "something", "desired_object", "animal" como subject.

RELACIONES ESPACIALES → LAYOUT:
"sobre", "encima de", "arriba de"               → top_bottom
"debajo de", "abajo de", "bajo"                 → bottom_top
"a la izquierda de"                             → left_right
"a la derecha de"                               → right_left
"junto a", "al lado de", "con"                  → left_right
"arriba a la derecha" / "abajo a la izquierda"  → top_right_bottom_left
"arriba a la izquierda" / "abajo a la derecha"  → top_left_bottom_right
"abajo a la derecha" / "arriba a la izquierda"  → bottom_right_top_left
"abajo a la izquierda" / "arriba a la derecha"  → bottom_left_top_right
"en diagonal"                                   → top_left_bottom_right

REGLAS DEL CAMPO "message" — IMPORTANTE:
- Máximo 2 oraciones. Mensajes cortos. No excedas 120 caracteres.
- Si action es "draw": confirma qué vas a dibujar y dile que lo vea en la simulación.
- Si action es "unknown": da 2 ejemplos concretos de lo que sí puede pedir. Nunca más de 2 oraciones.

CUÁNDO USAR "unknown":
- Saludos, despedidas, agradecimientos: "hola", "adiós", "gracias"
- Preguntas sobre el sistema: "cómo funcinas", "qué eres", "cuánto mide el pizarrón"
- Comandos de control: "para", "detente", "cancela"

CUÁNDO USAR "draw" aunque sea vago:
- Pedidos abiertos → elige un objeto simple tú mismo
- Preguntas sobre qué puede dibujar → elige y dibuja algo

EJEMPLOS COMPLETOS:
"dibuja un perro"
→ {"action":"draw","subjects":["dog"],"layout":"center","message":"¡Listo! Dibujando un perro. Míralo en la simulación."}

"una manzana sobre una mesa"
→ {"action":"draw","subjects":["apple","table"],"layout":"top_bottom","message":"Dibujando una manzana sobre una mesa. Míralo en la simulación."}

"dibuja un árbol abajo a la derecha de un pájaro"
→ {"action":"draw","subjects":["tree","bird"],"layout":"bottom_right_top_left","message":"Dibujando un árbol abajo a la derecha del pájaro. Míralo en la simulación."}

"dibuja lo que quieras"
→ {"action":"draw","subjects":["star"],"layout":"center","message":"Va una estrella. Puedes verla en la simulación."}

"sorpréndeme"
→ {"action":"draw","subjects":["butterfly"],"layout":"center","message":"¡Va una mariposa! Mírala en la simulación."}

"hola cómo estás"
→ {"action":"unknown","message":"Pídeme que dibuje algo. Ejemplo: 'dibuja un gato'."}

"gracias"
→ {"action":"unknown","message":"Pídeme que dibuje algo. Por ejemplo: 'hazme una flor' o 'dibuja un pájaro'."}"""

CASOS = [
    # Un objeto
    ("single_1",        "dibuja un perro"),
    ("single_2",        "hazme una mariposa"),
    ("single_3",        "trázame un elefante"),

    # Horizontal
    ("horiz_1",         "dibuja un gato junto a un perro"),
    ("horiz_2",         "un pájaro a la izquierda de un árbol"),
    ("horiz_3",         "pon un pez a la derecha de una roca"),

    # Vertical
    ("vert_1",          "una manzana sobre una mesa"),
    ("vert_2",          "dibuja un libro encima de una silla"),
    ("vert_3",          "quiero un perro debajo de un árbol"),

    # Diagonal
    ("diag_1",          "un sol en diagonal con una luna"),
    ("diag_2",          "dibuja un pájaro arriba a la derecha y un pez abajo a la izquierda"),
    ("diag_3",          "dibuja un árbol abajo a la derecha de un pájaro"),

    # Naturales
    ("natural_1",       "quiero un perrito junto a su casita"),
    ("natural_2",       "un pájaro posado sobre una rama"),
    ("natural_3",       "dibuja una manzana encima de una mesa como si estuviera de adorno"),

    # Vagos — deben intentar dibujar
    ("vago_1",          "algo bonito"),
    ("vago_2",          "dibuja lo que quieras"),
    ("vago_3",          "sorpréndeme"),
    ("vago_4",          "qué puedes dibujar"),

    # No dibujo — deben ser unknown
    ("no_dibujo_1",     "hola cómo estás"),
    ("no_dibujo_2",     "para ya"),
    ("no_dibujo_3",     "gracias"),
    ("no_dibujo_4",     "cuánto mide el pizarrón"),
    ("no_dibujo_5",     "cómo funcinas"),
]

LAYOUTS_VALIDOS = {
    "center", "left_right", "right_left",
    "top_bottom", "bottom_top",
    "top_left_bottom_right", "top_right_bottom_left",
    "bottom_left_top_right", "bottom_right_top_left"
}

MUST_BE_UNKNOWN = {"no_dibujo_1", "no_dibujo_2", "no_dibujo_3", "no_dibujo_4", "no_dibujo_5"}
MUST_BE_DRAW    = {"vago_1", "vago_2", "vago_3", "vago_4"}

def extraer_json(texto):
    # Intento 1: parseo directo
    try:
        return json.loads(texto)
    except json.JSONDecodeError:
        pass

    # Intento 2: extraer el primer bloque {} con regex
    match = re.search(r'\{.*?\}', texto, re.DOTALL)
    if match:
        try:
            return json.loads(match.group())
        except json.JSONDecodeError:
            pass

    # Intento 3: el JSON está incompleto, intentar cerrarlo
    # Busca desde el primer { hasta el final del texto
    match = re.search(r'\{.*', texto, re.DOTALL)
    if match:
        fragmento = match.group()
        # Contar llaves para ver cuántas faltan cerrar
        abiertas = fragmento.count('{') - fragmento.count('}')
        # Cerrar strings sin cerrar
        if fragmento.count('"') % 2 != 0:
            fragmento += '"'
        # Cerrar llaves faltantes
        fragmento += '}' * abiertas
        try:
            return json.loads(fragmento)
        except json.JSONDecodeError:
            pass

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
    if "action" not in resultado:
        return "FALLO_SIN_ACTION"

    # Verificar que existe message
    mensaje = resultado.get("message", "")
    if not mensaje or not isinstance(mensaje, str) or len(mensaje.strip()) == 0:
        return "FALLO_SIN_MESSAGE"

    accion = resultado["action"]

    if nombre in MUST_BE_UNKNOWN and accion != "unknown":
        return f"FALLO_DEBE_SER_UNKNOWN (fue '{accion}')"

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
    print(f"  Prueba: {MODELO}  |  JSON + message en español")
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
        print(f"  Input:    '{texto}'")

        if resultado:
            # Mostrar JSON sin message para no saturar la vista
            datos_robot = {k: v for k, v in resultado.items() if k != "message"}
            print(f"  Robot:    {json.dumps(datos_robot, ensure_ascii=False)}")
            print(f"  App msg:  \"{resultado.get('message', '')}\"")
        else:
            print(f"  Error:    {error}")

        print(f"  Estado:   {estado}  |  Latencia: {latencia:.2f}s")
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
        print("\n  Todos los casos pasaron. Bloque 2 completamente aprobado.")
    print()