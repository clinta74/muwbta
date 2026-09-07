import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'

// Before App, and that is the whole reason it is here rather than in App.tsx. CSS lands in the
// bundle in module-evaluation order, so importing the design system alongside the component that
// happens to need it first would put a feature sheet ahead of the base rules it is written to sit
// on top of — and which of the two won would depend on which screen was imported first.
import './styles/index.scss'

import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
)
