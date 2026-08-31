"use client";

import { motion, useReducedMotion, type ForwardRefComponent, type HTMLMotionProps } from "motion/react";
import { type ReactNode, useMemo } from "react";

interface FadeContentProps {
  children: ReactNode;
  blur?: boolean;
  duration?: number;
  delay?: number;
  className?: string;
  as?: keyof React.JSX.IntrinsicElements;
}

export default function FadeContent({
  children,
  blur = false,
  duration = 0.8,
  delay = 0,
  className = "",
  as: Tag = "div",
}: FadeContentProps) {
  const reduce = useReducedMotion();

  const MotionComponent = useMemo(() => {
    return motion.create(Tag as string) as ForwardRefComponent<
      React.ElementRef<typeof Tag>,
      HTMLMotionProps<"div">
    >;
  }, [Tag]);

  if (reduce) {
    return <Tag className={className}>{children}</Tag>;
  }

  return (
    <MotionComponent
      initial={{ opacity: 0, y: 24, filter: blur ? "blur(10px)" : "blur(0px)" }}
      animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
      transition={{
        duration,
        delay,
        ease: [0.16, 1, 0.3, 1],
      }}
      className={className}
    >
      {children}
    </MotionComponent>
  );
}
