import requests
import json
import time
import re

MODELO = "phi3.5"
URL_OLLAMA = "http://localhost:11434/api/chat"

PROMPT_SISTEMA = """Eres un intérprete de comandos de dibujo robótico.
Responde ÚNICAMENTE con un objeto JSON. Nada más. Cero texto extra.

REGLAS:
- Un objeto: {"action": "draw", "subjects": ["nombre_en_ingles"], "layout": "center"}
- Dos objetos: {"action": "draw", "subjects": ["objeto1", "objeto2"], "layout": "side_by_side"}
- No es dibujo: {"action": "unknown"}
- subjects SIEMPRE en inglés minúsculas.
- PROHIBIDO escribir cualquier cosa fuera del JSON."""

CASOS = [
    ("directo_simple_1",  "dibuja un perro"),
    ("directo_simple_2",  "dibuja un gato"),
    ("directo_simple_3",  "dibuja una flor"),
    ("directo_simple_4",  "dibuja una casa"),
    ("fraseo_alt_1",      "quiero ver un perro"),
    ("fraseo_alt_2",      "trázame un pájaro"),
    ("fraseo_alt_3",      "hazme un conejo"),
    ("fraseo_alt_4",      "pon un árbol"),
    ("fraseo_alt_5",      "me puedes hacer un pez"),
    ("fraseo_alt_6",      "a ver cómo dibujas un elefante"),
    ("escena_1",          "dibuja un perro junto a una casa"),
    ("escena_2",          "quiero un gato y un pájaro"),
    ("escena_3",          "pon un árbol con una flor"),
    ("ambiguo_1",         "algo bonito"),
    ("ambiguo_2",         "dibuja lo que quieras"),
    ("ambiguo_3",         "un animal"),
    ("no_dibujo_1",       "hola cómo estás"),
    ("no_dibujo_2",       "qué puedes hacer"),
    ("no_dibujo_3",       "para ya"),
    ("fuera_catalogo_1",  "dibuja un Lamborghini"),
    ("fuera_catalogo_2",  "dibuja la torre Eiffel"),
    ("informal_1",        "órale ponme un perrito"),
    ("informal_2",        "wey dibuja un gato"),
    ("informal_3",        "va, una mariposa"),
]

def extraer_json(texto):
    """Intenta parsear directo; si falla, busca el primer bloque {} en el texto."""
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
            "format": "json",          # <-- fuerza JSON puro
            "messages": [
                {"role": "system", "content": PROMPT_SISTEMA},
                {"role": "user",   "content": texto}
            ]
        }, timeout=30)
        latencia = time.time() - inicio
        contenido = response.json()["message"]["content"].strip()
        resultado = extraer_json(contenido)

        if resultado is None:
            return None, latencia, f"No se pudo extraer JSON de: {contenido}"
        return resultado, latencia, None

    except Exception as e:
        return None, time.time() - inicio, f"Error: {e}"

def evaluar(resultado):
    if resultado is None:
        return "FALLO_JSON"
    if "action" not in resultado:
        return "FALLO_SIN_ACTION"
    if resultado["action"] == "draw":
        if "subjects" not in resultado or not isinstance(resultado["subjects"], list):
            return "FALLO_SIN_SUBJECTS"
        if len(resultado["subjects"]) == 0:
            return "FALLO_SUBJECTS_VACIO"
        if "layout" not in resultado:
            return "FALLO_SIN_LAYOUT"
        return "OK"
    if resultado["action"] == "unknown":
        return "OK_UNKNOWN"
    return "FALLO_ACTION_INVALIDO"

if __name__ == "__main__":
    print(f"\n{'='*65}")
    print(f"  Prueba de LLM: {MODELO}  |  format=json activado")
    print(f"{'='*65}\n")

    ok_count    = 0
    fallos      = []
    latencias   = []

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
            fallos.append((nombre, estado))

    total = len(CASOS)
    lat_prom = sum(latencias) / len(latencias)

    print(f"{'='*65}")
    print(f"  Resultado:       {ok_count}/{total} correctos ({ok_count/total*100:.0f}%)")
    print(f"  Latencia prom:   {lat_prom:.2f}s")
    print(f"  Latencia máx:    {max(latencias):.2f}s")
    print(f"{'='*65}")

    if fallos:
        print("\n  Casos que fallaron:")
        for nombre, estado in fallos:
            print(f"  - {nombre}: {estado}")
    print()