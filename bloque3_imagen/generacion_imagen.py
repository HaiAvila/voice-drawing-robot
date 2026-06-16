import cv2
import numpy as np
import base64
import requests
import os
from dotenv import load_dotenv
from openai import OpenAI
import cv2

load_dotenv("openai.env") 

client = OpenAI()

# =====================================================================
# CASOS DE PRUEBA (Descomenta el que quieras probar)
# =====================================================================
#objects = []                                                 # Caso 0: Vacío
#objects = ["airplane"]                                       # Caso 1: Un elemento
#objects = ["duck", "lake"]                                   # Caso 2: Dos elementos
#objects = ["car", "motorcycle", "motorcycle"]                # Caso 3: Tres elementos (con duplicados)
#objects = ["tree", "apple", "apple", "house", "beach"]      # Caso 4: Muchos elementos

# --- FASE 1: IA ---
peticion_usuario = ["car", "motorcycle", "motorcycle"]

if len(peticion_usuario) == 0:
    peticion_usuario = "nothing"
elif len(peticion_usuario) == 1:
    peticion_usuario = peticion_usuario[0]
else:
    # Une los elementos con comas (ej: "car, motorcycle, motorcycle")
    peticion_usuario = ", ".join(peticion_usuario)

# 2. Enrutamiento del Prompt (La Magia)
if len(peticion_usuario) == 1:
    # Prompt estricto para objetos individuales (evita que invente cosas)
    MASTER_PROMPT = (
        f"exactly one solid black silhouette of a {peticion_usuario}, "
        "pure white background, isolated, completely flat, highly simplified vector shape, minimalist, no shading, no complex details"
    )
else:
    # Prompt de composición para múltiples objetos
    MASTER_PROMPT = (
        f"A single cohesive solid black silhouette scene containing ALL of these elements: {peticion_usuario}. "
        "All elements must be clearly visible and merge into one continuous flat vector composition. "
        "Highly simplified, iconic flat vector art. Extremely minimalist forms. "
        "Absolutely NO complex textures, NO interior patterns, NO detailed rendering, and NO shading. "
        "The forms are purely flat, solid black masses with smooth, simple outlines. "
        "Pure white background, completely flat, no nature backgrounds or anytype."
    )

print(f"Generando imagen con OpenAI para: {peticion_usuario}...")
response = client.images.generate(
    model="gpt-image-2", 
    prompt=MASTER_PROMPT,
    size="1024x1024",
    n=1,
)

# DIAGNÓSTICO: Inspeccionar la respuesta cruda de la API
datos_imagen = response.data[0]
print("\n--- INICIO DE RESPUESTA DE LA API ---")
print(datos_imagen)
print("--- FIN DE RESPUESTA ---\n")

# Extracción dinámica
img_data = None
if getattr(datos_imagen, 'b64_json', None):
    print("Éxito: La imagen viene en Base64. Decodificando...")
    img_data = base64.b64decode(datos_imagen.b64_json)
elif getattr(datos_imagen, 'url', None):
    print("Éxito: La API entregó una URL válida. Descargando...")
    img_data = requests.get(datos_imagen.url).content
else:
    print("ERROR CRÍTICO: No se encontró ni 'url' ni 'b64_json' en los datos.")

if img_data:
    with open("1_silueta_ia.png", "wb") as handler:
        handler.write(img_data)
    print("Silueta guardada exitosamente.")

    # --- FASE 2: OPENCV ---
    print("Procesando trazo industrial (curvas suaves)...")
   
   # Cargamos la silueta perfecta que generó la IA
    opencv_image = cv2.imread("1_silueta_ia.png")
    gray = cv2.cvtColor(opencv_image, cv2.COLOR_BGR2GRAY)
    
    # 1. Umbralización binarizada (Inversa)
    _, thresh = cv2.threshold(gray, 127, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)
    
    # 2. Extracción de Contornos EXACTOS
    contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    
    # 3. Crear lienzo blanco para el robot
    canvas = np.ones((1024, 1024, 3), dtype=np.uint8) * 255
    
    # 4. Dibujar SOLO el contorno
    cv2.drawContours(canvas, contours, -1, (0, 0, 0), 1)
    
    # Guardar el trazo definitivo
    cv2.imwrite("2_trazo_robot.png", canvas)
    print("Éxito! Trazo fiel y suave guardado como 2_trazo_robot.png.")