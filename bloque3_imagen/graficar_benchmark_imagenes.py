import argparse
from pathlib import Path

import pandas as pd
import matplotlib.pyplot as plt


def cargar_csv(ruta_csv: str) -> pd.DataFrame:
    df = pd.read_csv(ruta_csv)

    columnas_numericas = [
        "generation_time_s",
        "steps_per_second",
        "num_inference_steps",
        "guidance_scale",
        "prompt_word_count",
        "quality_score",
    ]

    for col in columnas_numericas:
        if col in df.columns:
            df[col] = pd.to_numeric(df[col], errors="coerce")

    # Alias para compatibilidad con funciones que usan latency_ms
    df["latency_ms"] = df["generation_time_s"] * 1000

    df = df.dropna(subset=["latency_ms"])
    df = df[df["latency_ms"] > 0]

    return df


def limpiar_nombre_archivo(texto: str) -> str:
    reemplazos = {
        "/": "_", "\\": "_", ":": "_", " ": "_",
        ".": "_", "=": "_", ",": "_", "(": "", ")": "",
    }
    texto = str(texto)
    for viejo, nuevo in reemplazos.items():
        texto = texto.replace(viejo, nuevo)
    return texto.lower()


# ── Gráfica 1: Tiempo de generación por sujeto, comparando modelos ──
def grafica_tiempo_por_sujeto(df: pd.DataFrame, out_dir: Path) -> None:
    if "subject_id" not in df.columns:
        return

    modelos   = df["model"].unique()
    sujetos   = df["subject_id"].unique()
    x         = range(len(sujetos))
    ancho     = 0.25
    fig, ax   = plt.subplots(figsize=(13, 7))

    for i, modelo in enumerate(modelos):
        datos_modelo = df[df["model"] == modelo]
        tiempos = [
            datos_modelo[datos_modelo["subject_id"] == s]["generation_time_s"].mean()
            for s in sujetos
        ]
        offset = (i - len(modelos) / 2 + 0.5) * ancho
        ax.bar(
            [xi + offset for xi in x],
            tiempos,
            width=ancho,
            label=modelo,
            alpha=0.85,
        )

    ax.set_xticks(list(x))
    ax.set_xticklabels(sujetos, rotation=25, ha="right")
    ax.set_title("Tiempo de generación por sujeto y modelo")
    ax.set_xlabel("Sujeto")
    ax.set_ylabel("Tiempo (segundos)")
    ax.legend()
    ax.grid(True, axis="y")

    fig.tight_layout()
    fig.savefig(out_dir / "tiempo_por_sujeto_y_modelo.png", dpi=160)
    plt.close(fig)
    print("  → tiempo_por_sujeto_y_modelo.png")


# ── Gráfica 2: Tiempo promedio por modelo con desviación estándar ──
def grafica_tiempo_promedio_por_modelo(df: pd.DataFrame, out_dir: Path) -> None:
    resumen = (
        df.groupby("model")
        .agg(
            media=("generation_time_s", "mean"),
            desviacion=("generation_time_s", "std"),
            n=("subject_id", "count"),
        )
        .reset_index()
        .sort_values("media")
    )

    labels = [
        f"{row.model}\n(n={int(row.n)})"
        for row in resumen.itertuples(index=False)
    ]

    fig, ax = plt.subplots(figsize=(10, 7))
    ax.bar(
        labels,
        resumen["media"],
        yerr=resumen["desviacion"],
        capsize=6,
        alpha=0.85,
    )
    ax.set_title("Tiempo de generación promedio por modelo ± desviación estándar")
    ax.set_xlabel("Modelo")
    ax.set_ylabel("Tiempo promedio (segundos)")
    ax.grid(True, axis="y")

    fig.tight_layout()
    fig.savefig(out_dir / "tiempo_promedio_por_modelo.png", dpi=160)
    plt.close(fig)
    print("  → tiempo_promedio_por_modelo.png")


# ── Gráfica 3: Calidad promedio por modelo ──
def grafica_calidad_por_modelo(df: pd.DataFrame, out_dir: Path) -> None:
    if "quality_score" not in df.columns:
        return

    data_plot = df.dropna(subset=["quality_score"])
    if data_plot.empty:
        return

    resumen = (
        data_plot.groupby("model")
        .agg(
            media=("quality_score", "mean"),
            desviacion=("quality_score", "std"),
            n=("subject_id", "count"),
        )
        .reset_index()
        .sort_values("media", ascending=False)
    )

    labels = [
        f"{row.model}\n(n={int(row.n)})"
        for row in resumen.itertuples(index=False)
    ]

    fig, ax = plt.subplots(figsize=(10, 7))
    ax.bar(
        labels,
        resumen["media"],
        yerr=resumen["desviacion"],
        capsize=6,
        alpha=0.85,
    )
    ax.set_ylim(0, 5.5)
    ax.set_title("Calidad promedio por modelo (1–5) ± desviación estándar")
    ax.set_xlabel("Modelo")
    ax.set_ylabel("Calidad promedio")
    ax.grid(True, axis="y")

    fig.tight_layout()
    fig.savefig(out_dir / "calidad_promedio_por_modelo.png", dpi=160)
    plt.close(fig)
    print("  → calidad_promedio_por_modelo.png")


# ── Gráfica 4: Calidad por sujeto, comparando modelos ──
def grafica_calidad_por_sujeto(df: pd.DataFrame, out_dir: Path) -> None:
    if "quality_score" not in df.columns or "subject_id" not in df.columns:
        return

    data_plot = df.dropna(subset=["quality_score"])
    if data_plot.empty:
        return

    modelos = data_plot["model"].unique()
    sujetos = data_plot["subject_id"].unique()
    x       = range(len(sujetos))
    ancho   = 0.25
    fig, ax = plt.subplots(figsize=(13, 7))

    for i, modelo in enumerate(modelos):
        datos_modelo = data_plot[data_plot["model"] == modelo]
        calidades = [
            datos_modelo[datos_modelo["subject_id"] == s]["quality_score"].mean()
            for s in sujetos
        ]
        offset = (i - len(modelos) / 2 + 0.5) * ancho
        ax.bar(
            [xi + offset for xi in x],
            calidades,
            width=ancho,
            label=modelo,
            alpha=0.85,
        )

    ax.set_xticks(list(x))
    ax.set_xticklabels(sujetos, rotation=25, ha="right")
    ax.set_ylim(0, 5.5)
    ax.set_title("Calidad por sujeto y modelo (1–5)")
    ax.set_xlabel("Sujeto")
    ax.set_ylabel("Calidad")
    ax.legend()
    ax.grid(True, axis="y")

    fig.tight_layout()
    fig.savefig(out_dir / "calidad_por_sujeto_y_modelo.png", dpi=160)
    plt.close(fig)
    print("  → calidad_por_sujeto_y_modelo.png")


# ── Gráfica 5: Steps por segundo por modelo (velocidad real) ──
def grafica_steps_por_segundo(df: pd.DataFrame, out_dir: Path) -> None:
    if "steps_per_second" not in df.columns:
        return

    data_plot = df.dropna(subset=["steps_per_second"])
    if data_plot.empty:
        return

    resumen = (
        data_plot.groupby("model")
        .agg(
            media=("steps_per_second", "mean"),
            desviacion=("steps_per_second", "std"),
            n=("subject_id", "count"),
        )
        .reset_index()
        .sort_values("media", ascending=False)
    )

    labels = [
        f"{row.model}\n(n={int(row.n)})"
        for row in resumen.itertuples(index=False)
    ]

    fig, ax = plt.subplots(figsize=(10, 7))
    ax.bar(
        labels,
        resumen["media"],
        yerr=resumen["desviacion"],
        capsize=6,
        alpha=0.85,
    )
    ax.set_title("Pasos de inferencia por segundo por modelo ± desviación estándar")
    ax.set_xlabel("Modelo")
    ax.set_ylabel("Steps/s")
    ax.grid(True, axis="y")

    fig.tight_layout()
    fig.savefig(out_dir / "steps_por_segundo_por_modelo.png", dpi=160)
    plt.close(fig)
    print("  → steps_por_segundo_por_modelo.png")


# ── Gráfica 6: Boxplot de tiempo de generación por modelo ──
def grafica_boxplot_tiempo(df: pd.DataFrame, out_dir: Path) -> None:
    modelos = list(df["model"].dropna().unique())
    if not modelos:
        return

    data   = [df[df["model"] == m]["generation_time_s"].dropna() for m in modelos]
    labels = [
        f"{m}\n(n={len(df[df['model'] == m])})"
        for m in modelos
    ]

    fig, ax = plt.subplots(figsize=(10, 7))
    ax.boxplot(data, labels=labels, showmeans=True)
    ax.set_title("Distribución del tiempo de generación por modelo")
    ax.set_xlabel("Modelo")
    ax.set_ylabel("Tiempo (segundos)")
    ax.grid(True, axis="y")

    fig.tight_layout()
    fig.savefig(out_dir / "boxplot_tiempo_por_modelo.png", dpi=160)
    plt.close(fig)
    print("  → boxplot_tiempo_por_modelo.png")


# ── Gráfica 7: Tiempo vs calidad (scatter) ──
def grafica_tiempo_vs_calidad(df: pd.DataFrame, out_dir: Path) -> None:
    if "quality_score" not in df.columns:
        return

    data_plot = df.dropna(subset=["quality_score", "generation_time_s"])
    if data_plot.empty:
        return

    fig, ax = plt.subplots(figsize=(10, 7))

    for modelo, data in data_plot.groupby("model"):
        ax.scatter(
            data["generation_time_s"],
            data["quality_score"],
            s=80,
            alpha=0.85,
            label=modelo,
        )

    ax.set_title("Tiempo de generación vs Calidad por modelo")
    ax.set_xlabel("Tiempo de generación (segundos)")
    ax.set_ylabel("Calidad (1–5)")
    ax.set_ylim(0, 5.5)
    ax.legend()
    ax.grid(True)

    fig.tight_layout()
    fig.savefig(out_dir / "tiempo_vs_calidad.png", dpi=160)
    plt.close(fig)
    print("  → tiempo_vs_calidad.png")


# ── Main ──────────────────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser(
        description="Genera gráficas de benchmark de modelos de generación de imágenes."
    )
    parser.add_argument("--csv", required=True, help="Archivo CSV de entrada.")
    parser.add_argument("--out", default="graficas_benchmark", help="Carpeta de salida.")
    args = parser.parse_args()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    df = cargar_csv(args.csv)

    print(f"\nArchivo leído: {args.csv}")
    print(f"Filas válidas: {len(df)}")
    print(f"Modelos encontrados: {list(df['model'].unique())}")
    print(f"Sujetos encontrados: {list(df['subject_id'].unique())}")
    print(f"\nGenerando gráficas en: {out_dir.resolve()}")

    grafica_tiempo_por_sujeto(df, out_dir)
    grafica_tiempo_promedio_por_modelo(df, out_dir)
    grafica_calidad_por_modelo(df, out_dir)
    grafica_calidad_por_sujeto(df, out_dir)
    grafica_steps_por_segundo(df, out_dir)
    grafica_boxplot_tiempo(df, out_dir)
    grafica_tiempo_vs_calidad(df, out_dir)

    print(f"\nListo. {7} gráficas guardadas en {out_dir.resolve()}")


if __name__ == "__main__":
    main()