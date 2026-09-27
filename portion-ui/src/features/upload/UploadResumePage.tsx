import { useCallback, useEffect, useRef, useState, type DragEvent } from 'react';
import { AlertTriangle, CheckCircle2, FileUp, Info, Upload } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { Button } from '../../components/ui/Button';
import { Panel } from '../../components/ui/Panel';
import { Spinner } from '../../components/ui/Spinner';
import { cn } from '../../lib/cn';
import { formatFileSize } from '../../lib/formatFileSize';
import { RESUME_FILE_ACCEPT } from '../../services/resumeApi';
import { useResumeUpload } from './hooks/useResumeUpload';

export function UploadResumePage() {
  const navigate = useNavigate();
  const { phase, progressLabel, result, lastFile, error, upload, reset } = useResumeUpload();
  const inputRef = useRef<HTMLInputElement>(null);
  const [isDragging, setIsDragging] = useState(false);

  const handleFile = useCallback(
    (file: File | null | undefined): void => {
      if (file === undefined || file === null) return;
      void upload(file);
    },
    [upload],
  );

  const onDrop = (event: DragEvent<HTMLLabelElement>): void => {
    event.preventDefault();
    setIsDragging(false);
    handleFile(event.dataTransfer.files.item(0) ?? undefined);
  };

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const isUploading = phase === 'uploading';

  return (
    <Panel
      variant="narrow"
      title="On-Demand Resume Ingress"
      description="Upload candidate CVs (.pdf, .docx, .txt). Files are queued and processed asynchronously by the API."
    >
      <label
        className={cn('drop-zone', isDragging && 'drop-zone--active', isUploading && 'drop-zone--busy')}
        htmlFor="file-input"
        onDragOver={(event) => {
          event.preventDefault();
          setIsDragging(true);
        }}
        onDragLeave={() => setIsDragging(false)}
        onDrop={onDrop}
      >
        <Upload size={44} className="drop-icon" aria-hidden="true" />
        <span className="drop-label">{isUploading ? 'Uploading…' : 'Click to select, or drop a resume here'}</span>
        <span className="drop-hint">PDF, DOCX or TXT · one file at a time</span>
        <input
          ref={inputRef}
          id="file-input"
          className="sr-only"
          type="file"
          accept={RESUME_FILE_ACCEPT}
          onChange={(event) => handleFile(event.target.files?.item(0))}
          disabled={isUploading}
        />
      </label>

      {isUploading ? (
        <div className="upload-pending" role="status">
          <Spinner size={16} label="Uploading" />
          <span>{progressLabel ?? 'Uploading…'}</span>
        </div>
      ) : null}

      {error !== null ? (
        <p className="upload-result upload-result--error" role="alert">
          <AlertTriangle size={15} aria-hidden="true" />
          <span>{error}</span>
        </p>
      ) : null}

      {result !== null ? (
        <div className={cn('upload-result', result.alreadyIndexed ? 'upload-result--info' : 'upload-result--ok')}>
          {result.alreadyIndexed ? <Info size={15} aria-hidden="true" /> : <CheckCircle2 size={15} aria-hidden="true" />}
          <div>
            <p className="upload-result__message">{result.message}</p>
            <p className="upload-result__meta">
              Resume id <code>{result.resumeId}</code> · {result.alreadyIndexed ? 'already indexed, no re-parse needed' : 'queued for indexing'}
              {lastFile !== null ? ` · ${lastFile.name} (${formatFileSize(lastFile.size)})` : ''}
            </p>
          </div>
        </div>
      ) : null}

      <div className="upload-actions">
        <Button variant="ghost" icon={<FileUp size={15} aria-hidden="true" />} onClick={() => inputRef.current?.click()} disabled={isUploading}>
          Choose file
        </Button>
        <Button variant="ghost" onClick={reset} disabled={isUploading || (result === null && error === null)}>
          Clear
        </Button>
        <Button variant="primary" onClick={() => navigate('/resumes')} disabled={result === null}>
          View registry
        </Button>
      </div>

      {result !== null ? (
        <p className="upload-size-note">
          Parsing and embedding happen server-side. The status flips to <strong>Synced</strong> once the chunks are vectorised.
        </p>
      ) : null}
    </Panel>
  );
}
