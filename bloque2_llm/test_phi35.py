import requests
import json
import time

# ─── Configuración ────────────────────────────────────────────────────────────

MODELO = "phi3.5"
URL_OLLAMA = "http://localhost:11434/api/chat"

PROMPT_SISTEMA = """Eres un intérprete de comandos de dibujo robótico.
El usuario te dirá qué quiere dibujar en lenguaje natural español.
Responde ÚNICAMENTE con JSON válido. Sin texto adicional. Sin markdown. Sin explicaciones.

Reglas:
- Si el usuario pide dibujar UN objeto: {"action": "draw", "subjects": ["nombre_en_ingles"], "layout": "center"}
- Si pide DOS objetos: {"action": "draw", "subjects": ["objeto1", "objeto2"], "layout": "side_by_side"}
- Si no es un pedido de dibujo: {"action": "unknown"}
- Los subjects SIEMPRE en inglés y en minúsculas.
- Responde SOLO con el JSON, nada más."""

# ─── Casos de prueba ──────────────────────────────────────────────────────────

CASOS = [
    # Comandos directos simples
    ("directo_simple_1",    "dibuja un perro"),
    ("directo_simple_2",    "dibuja un gato"),
    ("directo_simple_3",    "dibuja una flor"),
    ("directo_simple_4",    "dibuja una casa"),

    # Variaciones de fraseo — mismo significado
    ("fraseo_alt_1",        "quiero ver un perro"),
    ("fraseo_alt_2",        "trázame un pájaro"),
    ("fraseo_alt_3",        "hazme un conejo"),
    ("fraseo_alt_4",        "pon un árbol"),
    ("fraseo_alt_5",        "me puedes hacer un pez"),
    ("fraseo_alt_6",        "a ver cómo dibujas un elefante"),

    # Dos objetos / escenas
    ("escena_1",            "dibuja un perro junto a una casa"),
    ("escena_2",            "quiero un gato y un pájaro"),
    ("escena_3",            "pon un árbol con una flor"),

    # Comandos ambiguos
    ("ambiguo_1",           "algo bonito"),
    ("ambiguo_2",           "dibuja lo que quieras"),
    ("ambiguo_3",           "un animal"),

    # No son pedidos de dibujo
    ("no_dibujo_1",         "hola cómo estás"),
    ("no_dibujo_2",         "qué puedes hacer"),
    ("no_dibujo_3",         "para ya"),

    # Objetos que probablemente no están en QuickDraw
    ("fuera_catalogo_1",    "dibuja un Lamborghini"),
    ("fuera_catalogo_2",    "dibuja la torre Eiffel"),

    # Lenguaje informal / coloquial
    ("informal_1",          "órale ponme un perrito"),
    ("informal_2",          "wey dibuja un gato"),
    ("informal_3",          "va, una mariposa"),
]

# ─── Función principal ────────────────────────────────────────────────────────

def llamar_llm(texto):
    inicio = time.time()
    try:
        response = requests.post(URL_OLLAMA, json={
            "model": MODELO,
            "stream": False,
            "messages": [
                {"role": "system", "content": PROMPT_SISTEMA},
                {"role": "user",   "content": texto}
            ]
        }, timeout=30)
        latencia = time.time() - inicio
        contenido = response.json()["message"]["content"].strip()

        # Intentar parsear el JSON
        try:
            resultado = json.loads(contenido)
            return resultado, latencia, None
        except json.JSONDecodeError:
            return None, latencia, f"JSON inválido: {contenido}"

    except Exception as e:
        return None, time.time() - inicio, f"Error de conexión: {e}"


def evaluar_resultado(resultado, nombre_caso):
    """Revisa si el resultado tiene la forma esperada."""
    if resultado is None:
        return "FALLO_JSON"
    if "action" not in resultado:
        return "FALLO_SIN_ACTION"
    if resultado["action"] == "draw":
        if "subjects" not in resultado or not isinstance(resultado["subjects"], list):
            return "FALLO_SIN_SUBJECTS"
        if "layout" not in resultado:
            return "FALLO_SIN_LAYOUT"
        return "OK"
    if resultado["action"] == "unknown":
        return "OK_UNKNOWN"
    return "FALLO_ACTION_INVALIDO"


# ─── Ejecución ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    print(f"\n{'='*65}")
    print(f"  Prueba de LLM: {MODELO}")
    print(f"{'='*65}\n")

    resultados_ok    = []
    resultados_fallo = []

    for nombre, texto in CASOS:
        resultado, latencia, error = llamar_llm(texto)
        estado = evaluar_resultado(resultado, nombre)

        simbolo = "✓" if "OK" in estado else "✗"
        print(f"{simbolo} [{nombre}]")
        print(f"  Input:   '{texto}'")
        print(f"  Output:  {json.dumps(resultado, ensure_ascii=False) if resultado else error}")
        print(f"  Estado:  {estado}  |  Latencia: {latencia:.2f}s")
        print()

        if "OK" in estado:
            resultados_ok.append(nombre)
        else:
            resultados_fallo.append((nombre, estado, error))

    # Resumen
    total = len(CASOS)
    ok    = len(resultados_ok)
    print(f"{'='*65}")
    print(f"  Resultado: {ok}/{total} casos correctos ({ok/total*100:.0f}%)")
    print(f"{'='*65}")

    if resultados_fallo:
        print("\n  Casos que fallaron:")
        for nombre, estado, error in resultados_fallo:
            print(f"  - {nombre}: {estado}")
            if error:
                print(f"    {error}")

    print()