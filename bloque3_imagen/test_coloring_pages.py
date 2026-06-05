import torch
from diffusers import StableDiffusionPipeline

device = "cuda" if torch.cuda.is_available() else "cpu"
print(f"Usando: {device}")

pipeline = StableDiffusionPipeline.from_pretrained(
    "stablediffusionapi/coloring-pages",
    torch_dtype=torch.float16,
    safety_checker=None,
)
pipeline.to(device)

# ── Única variable que cambia por request ─────────────────────────
SUBJECT = "duck"  # dog | duck | house | turtle | flower | coffee mug | book | etc.
# ─────────────────────────────────────────────────────────────────

MASTER_PROMPT = (
    f"coloring book page, simple outline drawing of a {SUBJECT}, "
    "full body visible, side view, "
    "thin clean black outline on white background, "
    "only essential lines needed to identify the subject, "
    "no texture, no shading, no fill, no background elements, "
    "small centered figure, plenty of white space around it, "
    "not cropped, entire figure fits inside canvas"
)

NEGATIVE_PROMPT = (
    "cropped, cut off, partial body, zoomed in, filling entire frame, "
    "realistic, photo, detailed, shading, shadows, gradients, texture, "
    "color, background, ground, grass, water, scenery, "
    "filled black areas, solid black shapes, "
    "logo, icon, badge, circular frame, border, decorative elements, "
    "multiple objects, two objects"
)

image = pipeline(
    prompt=MASTER_PROMPT,
    negative_prompt=NEGATIVE_PROMPT,
    height=512,
    width=512,
    num_inference_steps=30,
    guidance_scale=9.0,
).images[0]

filename = f"coloring_{SUBJECT}.png"
image.save(filename)
print(f"Imagen guardada como {filename}")
