import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'

function App() {
  return (
    <BrowserRouter>
      <Toaster position="top-right" />
      <Routes>
        <Route path="/" element={<div className="p-4 text-lg">Dollars2</div>} />
        <Route path="/login" element={<div className="p-4 text-lg">Login</div>} />
      </Routes>
    </BrowserRouter>
  )
}

export default App
