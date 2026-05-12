// API Client connected to .NET Backend
const BASE_URL = 'http://localhost:5243/api/interviews';

export interface InterviewConfig {
  id: string;
  title: string;
  blocks: InterviewBlock[];
}

export interface InterviewBlock {
  id: string;
  title: string;
  durationSeconds: number;
  questions: Question[];
}

export interface Question {
  id: string;
  text: string;
  imageUrl?: string;
  checklist: string[];
}

export interface BlockState {
  blockId: string;
  notes: Record<string, string>; // questionId -> note
  checkedItems: Record<string, Record<string, boolean>>; // questionId -> checklistItem -> boolean
  ratings: Record<string, number>; // questionId -> rating (1-5)
  timeSpentSeconds: number;
}

export const api = {
  startInterview: async (code: string): Promise<{ interviewId: string, config: InterviewConfig }> => {
    const res = await fetch(`${BASE_URL}/start`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code })
    });
    if (!res.ok) throw new Error('Failed to start interview');
    return res.json();
  },
  
  saveBlockState: async (interviewId: string, state: BlockState): Promise<void> => {
    await fetch(`${BASE_URL}/${interviewId}/blocks/${state.blockId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        timeSpentSeconds: state.timeSpentSeconds,
        notes: state.notes,
        checkedItems: state.checkedItems,
        ratings: state.ratings
      })
    });
  },
  
  finishInterview: async (interviewId: string, summaryNotes: string, overallRating: number): Promise<void> => {
    await fetch(`${BASE_URL}/${interviewId}/finish`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ summaryNotes, overallRating })
    });
  }
};
