import { useState, useEffect } from 'react';
import { api } from './api';
import type { InterviewConfig, BlockState } from './api';
import './index.css';

function formatTime(seconds: number) {
  const m = Math.floor(seconds / 60).toString().padStart(2, '0');
  const s = (seconds % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

const StarRating = ({ value, onChange }: { value: number, onChange: (val: number) => void }) => {
  return (
    <div className="rating-container">
      <span className="rating-label">Rating:</span>
      {[1, 2, 3, 4, 5].map(star => (
        <span 
          key={star} 
          className={`star ${star <= value ? 'active' : ''}`}
          onClick={() => onChange(star)}
        >
          ★
        </span>
      ))}
    </div>
  );
};

export default function App() {
  const [sessionCode, setSessionCode] = useState("");
  const [interviewId, setInterviewId] = useState<string | null>(null);
  const [config, setConfig] = useState<InterviewConfig | null>(null);
  
  // App State
  const [isInterviewStarted, setIsInterviewStarted] = useState(false);
  const [isFinished, setIsFinished] = useState(false);
  const [isBlockActive, setIsBlockActive] = useState(false);
  const [showEndModal, setShowEndModal] = useState(false);
  
  // Timer State
  const [globalTimeElapsed, setGlobalTimeElapsed] = useState(0);
  const [blockTimeElapsed, setBlockTimeElapsed] = useState(0);
  
  // Data State
  const [activeBlockIndex, setActiveBlockIndex] = useState(0);
  const [blockStates, setBlockStates] = useState<Record<string, BlockState>>({});

  const [summaryNotes, setSummaryNotes] = useState("");
  const [overallRating, setOverallRating] = useState(0);

  // Timer tick
  useEffect(() => {
    if (!config || isFinished || !isInterviewStarted) return;
    const interval = setInterval(() => {
      setGlobalTimeElapsed(prev => prev + 1);
      if (isBlockActive) {
        setBlockTimeElapsed(prev => prev + 1);
      }
    }, 1000);
    return () => clearInterval(interval);
  }, [config, isFinished, isInterviewStarted, isBlockActive]);

  const handleStartInterview = async () => {
    if (!sessionCode.trim()) {
      alert("Please enter a session code");
      return;
    }
    
    try {
      const res = await api.startInterview(sessionCode);
      setInterviewId(res.interviewId);
      setConfig(res.config);
      
      const initialStates: Record<string, BlockState> = {};
      res.config.blocks.forEach(b => {
        initialStates[b.id] = {
          blockId: b.id,
          notes: {},
          checkedItems: {},
          ratings: {},
          timeSpentSeconds: 0
        };
      });
      setBlockStates(initialStates);
      
      setIsInterviewStarted(true);
      setIsBlockActive(true);
    } catch (err) {
      alert("Failed to start interview: " + err);
    }
  };

  const handleEndBlock = async () => {
    if (!config || !interviewId) return;
    setIsBlockActive(false);

    const activeBlock = config.blocks[activeBlockIndex];
    const currentState = { ...blockStates[activeBlock.id], timeSpentSeconds: blockTimeElapsed };
    setBlockStates(prev => ({ ...prev, [activeBlock.id]: currentState }));
    
    await api.saveBlockState(interviewId, currentState);
  };

  const handleStartNextBlock = async () => {
    if (!config) return;
    if (activeBlockIndex < config.blocks.length - 1) {
      setActiveBlockIndex(prev => prev + 1);
      setBlockTimeElapsed(0);
      setIsBlockActive(true);
    } else {
      setShowEndModal(true);
    }
  };

  const handleConfirmEndInterview = async () => {
    if (!interviewId) return;
    setIsFinished(true);
    setIsBlockActive(false);
    setShowEndModal(false);
    await api.finishInterview(interviewId, summaryNotes, overallRating);
  };

  const handleStateChange = (blockId: string, questionId: string, field: 'notes' | 'ratings', value: any) => {
    setBlockStates(prev => ({
      ...prev,
      [blockId]: {
        ...prev[blockId],
        [field]: {
          ...prev[blockId][field as any],
          [questionId]: value
        }
      }
    }));
  };

  const handleCheck = (blockId: string, questionId: string, checkId: string, checked: boolean) => {
    setBlockStates(prev => {
      const block = prev[blockId];
      const qChecks = block.checkedItems[questionId] || {};
      return {
        ...prev,
        [blockId]: {
          ...block,
          checkedItems: {
            ...block.checkedItems,
            [questionId]: {
              ...qChecks,
              [checkId]: checked
            }
          }
        }
      };
    });
  };

  if (isFinished) {
    return (
      <div className="app-container" style={{ alignItems: 'center', justifyContent: 'center' }}>
        <div className="question-card" style={{ textAlign: 'center' }}>
          <h2>Interview Completed</h2>
          <p style={{ marginTop: 16 }}>Total Time: {formatTime(globalTimeElapsed)}</p>
          <p>All notes and checklists have been saved to the server.</p>
        </div>
      </div>
    );
  }

  if (!isInterviewStarted) {
    return (
      <div className="app-container" style={{ alignItems: 'center', justifyContent: 'center' }}>
        <div className="question-card" style={{ textAlign: 'center', padding: '40px', maxWidth: '500px' }}>
          <h2 style={{ fontSize: '1.5rem', marginBottom: '8px' }}>Interviewer Platform</h2>
          <p style={{ color: 'var(--text-secondary)', marginBottom: '32px' }}>
            Please enter the unique interview session code below.
          </p>
          <div className="form-group">
            <input 
              type="text" 
              className="input-field" 
              placeholder="e.g. SWE-SENIOR-SQUAD-123" 
              value={sessionCode}
              onChange={(e) => setSessionCode(e.target.value)}
              style={{ textAlign: 'center', fontSize: '1.2rem', padding: '16px' }}
            />
          </div>
          <button className="btn btn-primary" style={{ fontSize: '1.2rem', padding: '12px 32px' }} onClick={handleStartInterview}>
            Start Interview
          </button>
        </div>
      </div>
    );
  }

  if (!config) return null;

  const activeBlock = config.blocks[activeBlockIndex];
  const activeState = blockStates[activeBlock.id];
  
  // Timer calculations
  const timeLeft = activeBlock.durationSeconds - blockTimeElapsed;
  let timerClass = 'normal';
  if (timeLeft <= 0) timerClass = 'danger';
  else if (timeLeft <= 30) timerClass = 'warning';

  return (
    <div className="app-container">
      {/* End Modal */}
      {showEndModal && (
        <div className="modal-overlay">
          <div className="modal-content">
            <h2>Finish Interview</h2>
            <div className="form-group">
              <label>Overall Rating</label>
              <StarRating value={overallRating} onChange={setOverallRating} />
            </div>
            <div className="form-group">
              <label>Summary Notes</label>
              <textarea 
                className="input-field" 
                rows={5}
                value={summaryNotes}
                onChange={(e) => setSummaryNotes(e.target.value)}
                placeholder="Final thoughts on the candidate..."
              ></textarea>
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '16px', marginTop: '24px' }}>
              <button className="btn btn-danger" onClick={() => setShowEndModal(false)}>Cancel</button>
              <button className="btn btn-primary" onClick={handleConfirmEndInterview}>Save & Complete</button>
            </div>
          </div>
        </div>
      )}

      {/* Sidebar */}
      <div className="sidebar">
        <div className="sidebar-header">
          <h2>{config.title}</h2>
          <div className="global-timer">Total Time: {formatTime(globalTimeElapsed)}</div>
        </div>
        <div className="block-list">
          {config.blocks.map((b, i) => {
            let className = 'block-item';
            if (i === activeBlockIndex) className += ' active';
            else if (i < activeBlockIndex) className += ' completed';
            
            return (
              <div key={b.id} className={className}>
                <div className="block-title">{b.title}</div>
                <div className="block-duration">Target: {Math.floor(b.durationSeconds / 60)} min</div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Main Content */}
      <div className="main-content">
        <div className="top-bar">
          <h2>{activeBlock.title} {!isBlockActive && <span style={{color: 'var(--warning-color)', fontSize: '1rem', marginLeft: '8px'}}>(Paused / Ended)</span>}</h2>
          <div className={`block-timer ${isBlockActive ? timerClass : 'normal'}`}>
            {formatTime(Math.abs(timeLeft))} {timeLeft < 0 ? 'overtime' : 'remaining'}
          </div>
        </div>

        <div className="content-area">
          {activeBlock.questions.map((q) => (
            <div key={q.id} className="question-card" style={{ opacity: isBlockActive ? 1 : 0.6, pointerEvents: isBlockActive ? 'auto' : 'none' }}>
              <div className="question-text">{q.text}</div>
              {q.imageUrl && (
                <img src={q.imageUrl} alt="Scenario" className="question-image" />
              )}
              
              <div className="checklist">
                {q.checklist.map((item, idx) => (
                  <label key={idx} className="checklist-item">
                    <input 
                      type="checkbox" 
                      checked={activeState.checkedItems[q.id]?.[item] || false}
                      onChange={(e) => handleCheck(activeBlock.id, q.id, item, e.target.checked)}
                    />
                    <span>{item}</span>
                  </label>
                ))}
              </div>

              <StarRating 
                value={activeState.ratings[q.id] || 0} 
                onChange={(val) => handleStateChange(activeBlock.id, q.id, 'ratings', val)} 
              />

              <textarea 
                className="notes-area" 
                placeholder="Take notes for this question..."
                value={activeState.notes[q.id] || ''}
                onChange={(e) => handleStateChange(activeBlock.id, q.id, 'notes', e.target.value)}
              />
            </div>
          ))}
        </div>

        <div className="actions-footer">
          <button className="btn btn-danger" onClick={() => setShowEndModal(true)}>End Interview</button>
          
          {isBlockActive ? (
            <button className="btn btn-primary" onClick={handleEndBlock}>
              End Block
            </button>
          ) : (
            <button className="btn btn-primary" onClick={handleStartNextBlock}>
              {activeBlockIndex === config.blocks.length - 1 ? 'Finish & Save Interview' : 'Start Next Block'}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
