import { Route, Routes } from 'react-router-dom'
import { AuthenticatedTemplate, UnauthenticatedTemplate } from '@azure/msal-react'
import { Layout } from './components/Layout'
import { SignInPage } from './pages/SignInPage'
import { ProblemListPage } from './pages/ProblemListPage'
import { ProblemDetailPage } from './pages/ProblemDetailPage'

export default function App() {
  return (
    <Layout>
      <UnauthenticatedTemplate>
        <SignInPage />
      </UnauthenticatedTemplate>

      <AuthenticatedTemplate>
        <Routes>
          <Route path="/" element={<ProblemListPage />} />
          <Route path="/problems/:slug" element={<ProblemDetailPage />} />
          <Route path="*" element={<ProblemListPage />} />
        </Routes>
      </AuthenticatedTemplate>
    </Layout>
  )
}
