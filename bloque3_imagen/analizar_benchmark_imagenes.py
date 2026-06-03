import pandas as pd

INPUT_CSV  = "benchmark_imagenes.csv"
OUTPUT_CSV = "resumen_benchmark_imagenes.csv"

# ── Cargar resultados ─────────────────────────────────────────────
results = pd.read_csv(INPUT_CSV)

# ── Convertir columnas numéricas ──────────────────────────────────
numeric_columns = [
    "generation_time_s",
    "steps_per_second",
    "num_inference_steps",
    "guidance_scale",
    "prompt_word_count",
    "quality_score",
]

for column in numeric_columns:
    if column in results.columns:
        results[column] = pd.to_numeric(results[column], errors="coerce")

# ── Columna equivalente a total_duration_s para compatibilidad
# con el graficador del profe
results["total_duration_s"] = results["generation_time_s"]

# ── Resumen estadístico por modelo ────────────────────────────────
agg_config = {
    "generation_time_s": ["mean", "std", "min", "max"],
    "steps_per_second":  ["mean", "std", "min", "max"],
    "num_inference_steps": ["mean"],
    "prompt_word_count": ["mean"],
    "quality_score":     ["mean", "std", "min", "max"],
}

# Solo incluir columnas que realmente existen en el CSV
agg_config = {
    col: funcs
    for col, funcs in agg_config.items()
    if col in results.columns
}

summary = results.groupby("model").agg(agg_config)
summary.columns = ["_".join(col).strip() for col in summary.columns.values]
summary = summary.reset_index()

print("Resumen del benchmark por modelo:")
print(summary.to_string(index=False))
print()

# ── Resumen adicional por sujeto (para ver qué tan difícil es cada uno) ──
if "subject_id" in results.columns and "quality_score" in results.columns:
    summary_subject = results.groupby("subject_id").agg(
        quality_mean=("quality_score", "mean"),
        quality_std=("quality_score", "std"),
        time_mean=("generation_time_s", "mean"),
        time_std=("generation_time_s", "std"),
        n=("model", "count"),
    ).reset_index()

    print("Resumen por sujeto:")
    print(summary_subject.to_string(index=False))
    print()

# ── Guardar ───────────────────────────────────────────────────────
summary.to_csv(OUTPUT_CSV, index=False, encoding="utf-8-sig")
print(f"Resumen guardado en {OUTPUT_CSV}")