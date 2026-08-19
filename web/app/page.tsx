import Link from "next/link";

export default function Page() {
  return (
    <div><h1>Page</h1> <br/>
    <Link href="/resume">resume</Link><br/>
    <Link href="/interview">interview</Link><br/>
    <Link href="/practice">practice</Link><br/>
    </div>
  );
}