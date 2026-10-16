FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

# Models are downloaded at first use into VOICE_MODELS_DIR (a compose volume),
# not baked into the image - see main.py.
COPY main.py ./

RUN useradd --create-home app && mkdir -p /models && chown app /models
ENV VOICE_MODELS_DIR=/models
USER app

EXPOSE 8001

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8001"]
