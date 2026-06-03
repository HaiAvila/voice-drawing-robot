import csv
import time
import torch
from datetime import datetime
from pathlib import Path
from diffusers import (
    KandinskyPipeline,
    KandinskyPriorPipeline,
    AutoPipelineForText2Image,
    FluxPipeline,
)
from optimum.quanto import freeze, qfloat8, quantize

# ── Configuración ────────────────────────────────────────────────
DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
DTYPE  = torch.float16

OUTPUT_CSV   = "benchmark_imagenes.csv"
OUTPUT_DIR   = Path("imagenes_generadas")
OUTPUT_DIR.mkdir(exist_ok=True)

NEGATIVE_PROMPT = (
    "color, shading, gradient, realistic, photo, detailed, "
    "complex background, watermark, text, 3d, shadow"
)

SUBJECTS = [
    {
        "id": "01_flor",
        "layout": "center",
        "prompt": (
            "simple black and white line drawing of a flower with round petals, "
            "children coloring book style, clean thick outlines, white background, "
            "no shading, no color fill, no texture, no details, minimalist flat design, "
            "single object centered"
        ),
    },
    {
        "id": "02_perro",
        "layout": "center",
        "prompt": (
            "simple black and white line drawing of a sitting dog with floppy ears, "
            "children coloring book style, clean thick outlines, white background, "
            "no shading, no color fill, no texture, no details, minimalist flat design, "
            "single object centered"
        ),
    },
    {
        "id": "03_casa",
        "layout": "center",
        "prompt": (
            "simple black and white line drawing of a house with a triangular roof "
            "and one window and one door, children coloring book style, clean thick "
            "outlines, white background, no shading, no color fill, no texture, "
            "no details, minimalist flat design, single object centered"
        ),
    },
    {
        "id": "04_manzana_libro",
        "layout": "top_bottom",
        "prompt": (
            "simple black and white line drawing of an apple on top of a book, "
            "children coloring book style, clean thick outlines, white background, "
            "no shading, no color fill, no texture, no details, minimalist flat design, "
            "apple at top, book at bottom, two objects stacked vertically"
        ),
    },
    {
        "id": "05_sol_casa",
        "layout": "top_right_bottom_left",
        "prompt": (
            "simple black and white line drawing of a sun with triangular rays "
            "and a small house, children coloring book style, clean thick outlines, "
            "white background, no shading, no color fill, no texture, no details, "
            "minimalist flat design, sun at top right corner, house at bottom left corner"
        ),
    },
    {
        "id": "06_pato_estanque",
        "layout": "left_right",
        "prompt": (
            "simple black and white line drawing of a duck next to a pond, "
            "children coloring book style, clean thick outlines, white background, "
            "no shading, no color fill, no texture, no details, minimalist flat design, "
            "duck on the left, pond on the right"
        ),
    },
]

FIELDNAMES = [
    "timestamp", "experiment_id", "model", "subject_id", "layout",
    "prompt", "negative_prompt", "num_inference_steps", "guidance_scale",
    "resolution", "generation_time_s", "steps_per_second",
    "prompt_word_count", "image_path", "quality_score", "notes",
]

# ── Cargadores de modelos ─────────────────────────────────────────

def load_kandinsky():
    print("Cargando Kandinsky 2.1...")
    prior = KandinskyPriorPipeline.from_pretrained(
        "kandinsky-community/kandinsky-2-1-prior",
        torch_dtype=DTYPE,
    ).to(DEVICE)
    pipe = KandinskyPipeline.from_pretrained(
        "kandinsky-community/kandinsky-2-1",
        torch_dtype=DTYPE,
    ).to(DEVICE)
    return {"prior": prior, "pipe": pipe}


def load_sdxl_turbo():
    print("Cargando SDXL-Turbo...")
    pipe = AutoPipelineForText2Image.from_pretrained(
        "stabilityai/sdxl-turbo",
        torch_dtype=DTYPE,
        variant="fp16",
    )
    pipe.enable_model_cpu_offload()   # importante para 4-6 GB VRAM
    return {"pipe": pipe}


def load_flux():
    print("Cargando Flux.1-schnell (cuantizado)...")
    pipe = FluxPipeline.from_pretrained(
        "black-forest-labs/FLUX.1-schnell",
        torch_dtype=torch.bfloat16,
    )
    quantize(pipe.transformer, weights=qfloat8)
    freeze(pipe.transformer)
    quantize(pipe.text_encoder_2, weights=qfloat8)
    freeze(pipe.text_encoder_2)
    pipe.enable_model_cpu_offload()
    return {"pipe": pipe}

# ── Generadores de imagen ─────────────────────────────────────────

def generate_kandinsky(components, subject):
    prior = components["prior"]
    pipe  = components["pipe"]
    steps = 50

    embeds, neg_embeds = prior(
        subject["prompt"],
        negative_prompt=NEGATIVE_PROMPT,
    ).to_tuple()

    start = time.perf_counter()
    image = pipe(
        subject["prompt"],
        image_embeds=embeds,
        negative_image_embeds=neg_embeds,
        height=512, width=512,
        num_inference_steps=steps,
    ).images[0]
    elapsed = time.perf_counter() - start

    return image, steps, elapsed


def generate_sdxl_turbo(components, subject):
    pipe  = components["pipe"]
    steps = 4

    start = time.perf_counter()
    image = pipe(
        prompt=subject["prompt"],
        negative_prompt=NEGATIVE_PROMPT,
        num_inference_steps=steps,
        guidance_scale=0.0,
    ).images[0]
    elapsed = time.perf_counter() - start

    return image, steps, elapsed


def generate_flux(components, subject):
    pipe  = components["pipe"]
    steps = 4

    start = time.perf_counter()
    image = pipe(
        subject["prompt"],
        guidance_scale=0.0,
        num_inference_steps=steps,
        max_sequence_length=256,
    ).images[0]
    elapsed = time.perf_counter() - start

    return image, steps, elapsed

# ── Loop principal ────────────────────────────────────────────────

MODELS = [
    ("kandinsky_2_1", load_kandinsky,    generate_kandinsky),
    ("sdxl_turbo",    load_sdxl_turbo,   generate_sdxl_turbo),
    ("flux_schnell",  load_flux,          generate_flux),
]

with open(OUTPUT_CSV, mode="w", newline="", encoding="utf-8") as f:
    writer = csv.DictWriter(f, fieldnames=FIELDNAMES)
    writer.writeheader()

    for model_name, loader, generator in MODELS:
        print(f"\n{'='*50}")
        print(f"Modelo: {model_name}")
        print(f"{'='*50}")

        components = loader()

        for subject in SUBJECTS:
            print(f"  Sujeto: {subject['id']}")

            row = {
                "timestamp":           datetime.now().isoformat(),
                "experiment_id":       "benchmark_imagenes_p2",
                "model":               model_name,
                "subject_id":          subject["id"],
                "layout":              subject["layout"],
                "prompt":              subject["prompt"],
                "negative_prompt":     NEGATIVE_PROMPT,
                "num_inference_steps": "",
                "guidance_scale":      "",
                "resolution":          "512x512",
                "generation_time_s":   "",
                "steps_per_second":    "",
                "prompt_word_count":   len(subject["prompt"].split()),
                "image_path":          "",
                "quality_score":       "",  # llenar manualmente después
                "notes":               "",
            }

            try:
                image, steps, elapsed = generator(components, subject)

                img_path = OUTPUT_DIR / f"{model_name}_{subject['id']}.png"
                image.save(img_path)

                row.update({
                    "num_inference_steps": steps,
                    "guidance_scale":      0.0,
                    "generation_time_s":   round(elapsed, 3),
                    "steps_per_second":    round(steps / elapsed, 3),
                    "image_path":          str(img_path),
                })

                print(f"    OK — {elapsed:.1f}s — guardada en {img_path}")

            except Exception as e:
                row["notes"] = str(e)
                print(f"    ERROR: {e}")

            writer.writerow(row)

        # Liberar VRAM antes de cargar el siguiente modelo
        del components
        torch.cuda.empty_cache()
        print(f"VRAM liberada después de {model_name}")

print(f"\nBenchmark terminado. CSV guardado en {OUTPUT_CSV}")