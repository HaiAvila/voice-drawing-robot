import torch
from transformers import AutoModelForSpeechSeq2Seq, AutoProcessor, pipeline

class TranscriptionService:
    def __init__(self):
        print("Cargando modelo Whisper large-v3-turbo...")
        
        # Detectar si hay una tarjeta gráfica (GPU) disponible
        self.device = "cuda:0" if torch.cuda.is_available() else "cpu"
        self.torch_dtype = torch.float16 if torch.cuda.is_available() else torch.float32
        
        # --- NUEVA LÍNEA DE CONFIRMACIÓN ---
        print(f"----> ATENCIÓN: El modelo se ejecutará en: {self.device.upper()} <----")
        
        model_id = "openai/whisper-large-v3-turbo"

        self.model = AutoModelForSpeechSeq2Seq.from_pretrained(
            model_id, 
            torch_dtype=self.torch_dtype, 
            low_cpu_mem_usage=True, 
            use_safetensors=True
        )
        self.model.to(self.device)

        self.processor = AutoProcessor.from_pretrained(model_id)

        self.pipe = pipeline(
            "automatic-speech-recognition",
            model=self.model,
            tokenizer=self.processor.tokenizer,
            feature_extractor=self.processor.feature_extractor,
            torch_dtype=self.torch_dtype,
            device=self.device,
        )
        print("¡Modelo Whisper cargado y listo!")

    def transcribir_audio(self, ruta_archivo_audio):
        """
        Toma la ruta de un archivo de audio y devuelve el texto transcrito.
        """
        try:
            # Como sabemos a priori que los comandos serán en español, 
            # pasamos el argumento language para mejorar el rendimiento.
            generate_kwargs = {"language": "spanish"}
            
            # Ejecutar la transcripción
            result = self.pipe(ruta_archivo_audio, generate_kwargs=generate_kwargs)
            
            texto_transcrito = result["text"].strip()
            print(f"Texto transcrito: {texto_transcrito}")
            return texto_transcrito
            
        except Exception as e:
            print(f"Error durante la transcripción: {e}")
            return None

# stt_service = TranscriptionService()
